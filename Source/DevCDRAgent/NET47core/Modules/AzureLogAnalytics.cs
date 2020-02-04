using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DevCDRAgent.Modules
{
    public class AzureLogAnalytics
    {
        public AzureLogAnalytics()
        {
            WorkspaceId = "";
            SharedKey = "";
            LogType = "";
        }
        public AzureLogAnalytics(string tennantid)
        {
            string sKeys = GetRegValue("software\\itnetX\\WriteAnalyticsLogs", tennantid, true);
            if (!string.IsNullOrEmpty(sKeys))
            {
                try
                {
                    WorkspaceId = sKeys.Split(';')[0] ?? "";
                    SharedKey = sKeys.Split(';')[1] ?? "";
                    LogType = sKeys.Split(';')[2] ?? "";
                    ApiVersion = "2016-04-01";
                }
                catch { }
            }
        }

        public String WorkspaceId { get; set; }
        public String SharedKey { get; set; }
        public String ApiVersion { get; set; }
        public String LogType { get; set; }
        public AzureLogAnalytics(String workspaceId, String sharedKey, String logType, String apiVersion = "2016-04-01")
        {
            this.WorkspaceId = workspaceId;
            this.SharedKey = sharedKey;
            this.LogType = logType;
            this.ApiVersion = apiVersion;
        }

        public void Post(string json)
        {
            Post(json, LogType);
        }

        public void Post(string json, string LogName)
        {
            try
            {
                if (string.IsNullOrEmpty(WorkspaceId))
                    return;

                string requestUriString = $"https://{WorkspaceId}.ods.opinsights.azure.com/api/logs?api-version={ApiVersion}";
                DateTime dateTime = DateTime.UtcNow;
                string dateString = dateTime.ToString("r");
                string signature = GetSignature("POST", json.Length, "application/json", dateString, "/api/logs");
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(requestUriString);
                request.ContentType = "application/json";
                request.Method = "POST";
                request.Headers["Log-Type"] = LogName;
                request.Headers["x-ms-date"] = dateString;
                request.Headers["Authorization"] = signature;
                byte[] content = Encoding.UTF8.GetBytes(json);
                using (Stream requestStreamAsync = request.GetRequestStream())
                {
                    requestStreamAsync.Write(content, 0, content.Length);
                }
                using (HttpWebResponse responseAsync = (HttpWebResponse)request.GetResponse())
                {
                    if (responseAsync.StatusCode != HttpStatusCode.OK && responseAsync.StatusCode != HttpStatusCode.Accepted)
                    {
                        Stream responseStream = responseAsync.GetResponseStream();
                        if (responseStream != null)
                        {
                            using (StreamReader streamReader = new StreamReader(responseStream))
                            {
                                throw new Exception(streamReader.ReadToEnd());
                            }
                        }
                    }
                }


            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void Post(Object oProps)
        {
            if (!string.IsNullOrEmpty(WorkspaceId))
            {
                Post(JsonConvert.SerializeObject(oProps));
            }
        }

        public void PostAsync(Object oProps)
        {
            if (!string.IsNullOrEmpty(WorkspaceId))
            {
                Task.Run(() => Post(JsonConvert.SerializeObject(oProps)));
            }
        }

        private string GetSignature(string method, int contentLength, string contentType, string date, string resource)
        {
            string message = $"{method}\n{contentLength}\n{contentType}\nx-ms-date:{date}\n{resource}";
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            using (HMACSHA256 encryptor = new HMACSHA256(Convert.FromBase64String(SharedKey)))
            {
                return $"SharedKey {WorkspaceId}:{Convert.ToBase64String(encryptor.ComputeHash(bytes))}";
            }
        }

        public static string GetRegValue(string Key, string Valuename, bool decrypt = false)
        {
            try
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(Key, false);
                string sResult = key.GetValue(Valuename, "").ToString();
                if (!string.IsNullOrEmpty(sResult))
                {
                    if (decrypt)
                        return StringCipher.Decrypt(sResult, Environment.MachineName + "salt");
                    else
                        return sResult;
                }

                return sResult;
            }
            catch
            {
                return "";
            }
        }

    }

    public static class StringCipher
    {
        // This constant is used to determine the keysize of the encryption algorithm in bits.
        // We divide this by 8 within the code below to get the equivalent number of bytes.
        private const int Keysize = 256;

        // This constant determines the number of iterations for the password bytes generation function.
        private const int DerivationIterations = 1000;

        public static string Encrypt(string plainText, string passPhrase)
        {
            // Salt and IV is randomly generated each time, but is preprended to encrypted cipher text
            // so that the same Salt and IV values can be used when decrypting.  
            var saltStringBytes = Generate256BitsOfRandomEntropy();
            var ivStringBytes = Generate256BitsOfRandomEntropy();
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
            {
                var keyBytes = password.GetBytes(Keysize / 8);
                using (var symmetricKey = new RijndaelManaged())
                {
                    symmetricKey.BlockSize = 256;
                    symmetricKey.Mode = CipherMode.CBC;
                    symmetricKey.Padding = PaddingMode.PKCS7;
                    using (var encryptor = symmetricKey.CreateEncryptor(keyBytes, ivStringBytes))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                            {
                                cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                                cryptoStream.FlushFinalBlock();
                                // Create the final bytes as a concatenation of the random salt bytes, the random iv bytes and the cipher bytes.
                                var cipherTextBytes = saltStringBytes;
                                cipherTextBytes = cipherTextBytes.Concat(ivStringBytes).ToArray();
                                cipherTextBytes = cipherTextBytes.Concat(memoryStream.ToArray()).ToArray();
                                memoryStream.Close();
                                cryptoStream.Close();
                                return Convert.ToBase64String(cipherTextBytes);
                            }
                        }
                    }
                }
            }
        }

        public static string Decrypt(string cipherText, string passPhrase)
        {
            // Get the complete stream of bytes that represent:
            // [32 bytes of Salt] + [32 bytes of IV] + [n bytes of CipherText]
            var cipherTextBytesWithSaltAndIv = Convert.FromBase64String(cipherText);
            // Get the saltbytes by extracting the first 32 bytes from the supplied cipherText bytes.
            var saltStringBytes = cipherTextBytesWithSaltAndIv.Take(Keysize / 8).ToArray();
            // Get the IV bytes by extracting the next 32 bytes from the supplied cipherText bytes.
            var ivStringBytes = cipherTextBytesWithSaltAndIv.Skip(Keysize / 8).Take(Keysize / 8).ToArray();
            // Get the actual cipher text bytes by removing the first 64 bytes from the cipherText string.
            var cipherTextBytes = cipherTextBytesWithSaltAndIv.Skip((Keysize / 8) * 2).Take(cipherTextBytesWithSaltAndIv.Length - ((Keysize / 8) * 2)).ToArray();

            using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
            {
                var keyBytes = password.GetBytes(Keysize / 8);
                using (var symmetricKey = new RijndaelManaged())
                {
                    symmetricKey.BlockSize = 256;
                    symmetricKey.Mode = CipherMode.CBC;
                    symmetricKey.Padding = PaddingMode.PKCS7;
                    using (var decryptor = symmetricKey.CreateDecryptor(keyBytes, ivStringBytes))
                    {
                        using (var memoryStream = new MemoryStream(cipherTextBytes))
                        {
                            using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                            {
                                var plainTextBytes = new byte[cipherTextBytes.Length];
                                var decryptedByteCount = cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length);
                                memoryStream.Close();
                                cryptoStream.Close();
                                return Encoding.UTF8.GetString(plainTextBytes, 0, decryptedByteCount);
                            }
                        }
                    }
                }
            }
        }

        private static byte[] Generate256BitsOfRandomEntropy()
        {
            var randomBytes = new byte[32]; // 32 Bytes will give us 256 bits.
            using (var rngCsp = new RNGCryptoServiceProvider())
            {
                // Fill the array with cryptographically secure random bytes.
                rngCsp.GetBytes(randomBytes);
            }
            return randomBytes;
        }
    }
}
