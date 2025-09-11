using System.Security.Cryptography;
using System.Text;

namespace Dzidek.Net.AutoUpgrade.Upgrader.FileUtils
{
    public static class FileMd5Retriever
    {
        public static string GetMd5(string filePath)
        {
            var fileBase64 = ConvertToBase64(filePath);
            return GetMD5(fileBase64);
        }

        private static string ConvertToBase64(string filePath)
        {
            // may throw exception if the file is used by another process and file share is blocked
            Byte[] bytes = ReadAllBytesSafe(filePath);
            return Convert.ToBase64String(bytes);
        }
        private static byte[] ReadAllBytesSafe(string filePath)
        {
            using (var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite)) 
            {
                byte[] buffer = new byte[fileStream.Length];
                fileStream.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }
        //MD5 hasher
        private static string GetMD5(string input)
        {
            var hashBytes = GetMD5Bytes(input);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
        private static byte[] GetMD5Bytes(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return hashBytes;
            }
        }
    }
}
