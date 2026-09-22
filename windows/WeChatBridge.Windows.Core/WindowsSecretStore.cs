using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WeChatBridge.Windows.Core;

/// <summary>
/// Protects the Notion token with Windows DPAPI. The protected value can only
/// be decrypted by the same Windows user on the same machine.
/// </summary>
public static class WindowsSecretStore
{
    private const uint CryptProtectUiForbidden = 0x1;

    public static string ProtectToBase64(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new NotionConfigurationException("Notion Token 不能为空。");
        }

        try
        {
            var protectedBytes = Protect(Encoding.UTF8.GetBytes(secret.Trim()));
            return Convert.ToBase64String(protectedBytes);
        }
        catch (CryptographicException exception)
        {
            throw new NotionConfigurationException("Windows 无法加密保存 Notion Token。", exception);
        }
    }

    public static string UnprotectFromBase64(string protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            throw new NotionConfigurationException("本机 Notion 配置中没有可用的 Token。");
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = Unprotect(protectedBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (FormatException exception)
        {
            throw new NotionConfigurationException("本机 Notion 配置文件已损坏，请在应用中重新保存 Token。", exception);
        }
        catch (CryptographicException exception)
        {
            throw new NotionConfigurationException(
                "本机 Notion Token 无法由当前 Windows 用户解密，请重新保存 Token。",
                exception);
        }
    }

    private static byte[] Protect(byte[] plainBytes)
    {
        EnsureWindows();
        var input = CreateBlob(plainBytes);
        try
        {
            if (!CryptProtectData(
                    ref input,
                    "WeChatBridge Notion Token",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            try
            {
                return ReadBlob(output);
            }
            finally
            {
                LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    private static byte[] Unprotect(byte[] protectedBytes)
    {
        EnsureWindows();
        var input = CreateBlob(protectedBytes);
        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    out var outputDescription,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            try
            {
                return ReadBlob(output);
            }
            finally
            {
                if (outputDescription != IntPtr.Zero)
                {
                    LocalFree(outputDescription);
                }

                LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob
        {
            cbData = bytes.Length,
            pbData = pointer,
        };
    }

    private static byte[] ReadBlob(DataBlob blob)
    {
        if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero)
        {
            throw new CryptographicException("Windows DPAPI 返回了空数据。");
        }

        var bytes = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new NotionConfigurationException("Notion Token 加密存储仅支持 Windows。");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        out IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
