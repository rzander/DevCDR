using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace DevCDR.Extensions
{
    public class SignatureVerification
    {
        public static string CreateSignature(string message, string certSubject)
        {
            try
            {
                var sout = Sign(message, certSubject);

                //create JSON
                JObject jObj = new JObject();
                jObj.Add(new JProperty("MSG", message));
                jObj.Add(new JProperty("SIG", sout));
                jObj.Add(new JProperty("CER", GetPublicCert(certSubject)));

                //create Base64 string
                return Convert.ToBase64String(Encoding.Default.GetBytes(jObj.ToString(Newtonsoft.Json.Formatting.None)));
            }
            catch (Exception ex)
            {

            }

            return "";
        }

        public static bool VerifySignature(string base64signature, string rootSubject, string rootCERFile = "")
        {
            try
            {
                JObject jObj = JObject.Parse(Encoding.Default.GetString(Convert.FromBase64String(base64signature)));
                return Verify(jObj["MSG"].Value<string>(), jObj["SIG"].Value<string>(), jObj["CER"].Value<string>(), rootSubject, rootCERFile);
            }
            catch { }

            return false;
        }

        public static X509Certificate2 getCert(string base64signature)
        {
            try
            {
                JObject jObj = JObject.Parse(Encoding.Default.GetString(Convert.FromBase64String(base64signature)));
                return new X509Certificate2(Convert.FromBase64String(jObj["CER"].Value<string>()));
            }
            catch { }

            return null;
        }

        internal static string getIssuingCA(X509Certificate2 cert)
        {
            string IssuingCA = "";
            try
            {
                //X509Chain ch = new X509Chain(true);
                //ch.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                //ch.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                //bool bChain = ch.Build(cert);
                //if (bChain)
                //{
                //    IssuingCA = ch.ChainElements[1].Certificate.Subject.Split('=')[1];
                //}
                //else
                //{
                //    IssuingCA = ch.ChainElements[1].Certificate.Subject.Split('=')[0];
                //}

                return cert.Issuer.Split('=')[1];
            }
            catch(Exception ex)
            { 
                return ex.Message; 
            }
            return IssuingCA;
        }
        internal static string getIssuingCA(string signature)
        {
            try
            {
                JObject jObj = JObject.Parse(Encoding.Default.GetString(Convert.FromBase64String(signature)));
                X509Certificate2 cert = new X509Certificate2(Convert.FromBase64String(jObj["CER"].Value<string>()));
                return getIssuingCA(cert);
            }
            catch(Exception ex) { return ex.Message; }

            return "";

        }

        internal static string findIssuingCA(string rootSubjectName)
        {
            try
            {
                X509Store my = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                my.Open(OpenFlags.ReadOnly);

                // Find the certificate we'll use to sign
                foreach (X509Certificate2 cert in my.Certificates.Find(X509FindType.FindByIssuerName, rootSubjectName, true))
                {
                    if (cert.Subject != cert.Issuer)
                        return cert.Subject.Split('=')[1];
                }
            }
            catch { }

            return "";

        }


        private static string Sign(string text, string certSubject)
        {
            // Access Personal (MY) certificate store of current user
            X509Store my = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            my.Open(OpenFlags.ReadOnly);

            // Find the certificate we'll use to sign
            foreach (X509Certificate2 cert in my.Certificates.Find(X509FindType.FindBySubjectName, certSubject, true))
            {
                if (cert.GetKeyAlgorithm() == "1.2.840.10045.2.1") //ECDsa Key
                {
                    using (ECDsa key = cert.GetECDsaPrivateKey())
                    {
                        if (key == null)
                        {
                            throw new Exception("No valid cert was found");
                        }

                        return Convert.ToBase64String(key.SignData(Encoding.Default.GetBytes(text), HashAlgorithmName.SHA256));
                    }
                }
                else
                {
                    using (RSA key = cert.GetRSAPrivateKey())
                    {
                        if (key == null)
                        {
                            throw new Exception("No valid cert was found");
                        }

                        return Convert.ToBase64String(key.SignData(Encoding.Default.GetBytes(text), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                    }
                }
            }

            throw new Exception("No valid cert was found");
        }

        private static string GetPublicCert(string certSubject)
        {
            // Access Personal (MY) certificate store of current user
            X509Store my = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            my.Open(OpenFlags.ReadOnly);

            // Find the certificate we'll use to sign
            foreach (X509Certificate2 cert in my.Certificates.Find(X509FindType.FindBySubjectName, certSubject, true))
            {
                return Convert.ToBase64String(cert.Export(X509ContentType.Cert));
            }

            throw new Exception("No valid cert was found");
        }

        internal static X509Certificate2 GetRootCert(string certSubject, string filePath = "")
        {
            if (string.IsNullOrEmpty(filePath))
            {
                // Access Personal (MY) certificate store of current user
                X509Store my = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                my.Open(OpenFlags.ReadOnly);

                // Find the certificate we'll use to sign
                foreach (X509Certificate2 cert in my.Certificates.Find(X509FindType.FindBySubjectName, certSubject, true))
                {
                    return cert;
                }
            }

            if(File.Exists(filePath))
            {
                return new X509Certificate2(filePath);
            }
            else
            {
                return new X509Certificate2(Convert.FromBase64String(filePath));
            }

            throw new Exception("No valid cert was found");
        }

        private static bool Verify(string text, string signature, string base64cert, string rootSubject, string rootCERFile = "")
        {
            X509Certificate2 cert = new X509Certificate2(Convert.FromBase64String(base64cert));

            //Output chain information of the selected certificate.
            X509Chain ch = new X509Chain();
            ch.ChainPolicy.ExtraStore.Add(new X509Certificate2(Convert.FromBase64String(rootCERFile)));
            ch.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            ch.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            //ch.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            //ch.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            bool bChain = ch.Build(cert);
            if (bChain)
            {
                X509Certificate2 validRootCertificate = new X509Certificate2(Convert.FromBase64String(rootCERFile)); // GetRootCert(rootSubject, rootCERFile);
                foreach (X509ChainElement element in ch.ChainElements)
                {
                    // Check that the root certificate matches
                    if (Convert.ToBase64String(validRootCertificate.RawData) == Convert.ToBase64String(element.Certificate.RawData))
                    {
                        if (cert.GetKeyAlgorithm() == "1.2.840.10045.2.1") //ECDsa Key
                        {
                            using (ECDsa key = cert.GetECDsaPublicKey())
                            {
                                return key.VerifyData(Encoding.Default.GetBytes(text), Convert.FromBase64String(signature), HashAlgorithmName.SHA256);
                            }
                        }
                        else
                        {
                            using (RSA key = cert.GetRSAPublicKey())
                            {
                                return key.VerifyData(Encoding.Default.GetBytes(text), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                            }
                        }
                    }
                }
            }

            return false;
        }
    }

    public class X509AgentCert
    {
        public static X509Certificate2Collection publicCertificates = new X509Certificate2Collection();
        public X509AgentCert(string deviceID, string customerID)
        {
            DeviceID = deviceID;
            CustomerID = customerID;
            X509Store my = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            my.Open(OpenFlags.ReadOnly);

            Exists = false;
            Expired = false;
            Valid = false;
            HasPrivateKey = false;

            // Find the certificate we'll use to sign
            foreach (X509Certificate2 cert in my.Certificates.Find(X509FindType.FindBySubjectName, DeviceID, true))
            {
                try
                {
                    Expired = false;
                    Exists = true;
                    Certificate = cert;
                    if (publicCertificates.Count > 0)
                        ValidateChain(publicCertificates);
                    else
                        ValidateChain();
                    HasPrivateKey = cert.HasPrivateKey;
                    Signature.ToString(); //generate Signature
                    break;
                }
                catch { }
            }

            if (!Exists)
            {
                foreach (X509Certificate2 cert in my.Certificates.Find(X509FindType.FindBySubjectName, DeviceID, false))
                {
                    if (cert.NotAfter <= DateTime.UtcNow)
                    {
                        Expired = true;
                        Exists = true;
                        Certificate = cert;
                        HasPrivateKey = cert.HasPrivateKey;
                    }
                    else
                    {
                        Exists = true;
                        Certificate = cert;
                        HasPrivateKey = cert.HasPrivateKey;
                        break;
                    }
                }
            }
        }

        public X509AgentCert(string signature , bool noCheck = false)
        {
            Exists = false;
            Expired = false;
            Valid = false;
            HasPrivateKey = false;

            try
            {
                JObject jObj = JObject.Parse(Encoding.Default.GetString(Convert.FromBase64String(signature)));
                try
                {
                    DeviceID = jObj["MSG"].Value<string>().Split(';')[0] ?? "";
                    CustomerID = jObj["MSG"].Value<string>().Split(';')[1] ?? "";
                }
                catch { }

                Certificate = new X509Certificate2(Convert.FromBase64String(jObj["CER"].Value<string>()));
                IssuingCA = Certificate.Issuer.Split('=')[1];
                Exists = true;
                
                if (!noCheck)
                {
                    if (publicCertificates.Count > 0)
                        ValidateChain(publicCertificates);
                    else
                        ValidateChain();
                    
                    Signature.ToString(); //generate Signature
                }
                HasPrivateKey = Certificate.HasPrivateKey;

                if (Certificate.NotAfter < DateTime.UtcNow)
                {
                    Expired = true;
                }
            }
            catch { }
        }

        public X509Certificate2 Certificate { get; set; }

        public bool ValidateChain()
        {
            try
            {
                X509Chain ch = new X509Chain(true);
                ch.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                ch.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                foreach (X509Certificate2 xPub in publicCertificates)
                {
                    ch.ChainPolicy.ExtraStore.Add(xPub);
                }

                bool bChain = ch.Build(Certificate);
                Chain = ch;
                Status = ch.ChainStatus;
                try
                {
                    IssuingCA = ch.ChainElements[1].Certificate.Subject.Split('=')[1];
                    RootCA = ch.ChainElements[2].Certificate.Subject.Split('=')[1];
                }
                catch { }
                Valid = bChain;
                return bChain;
            }
            catch
            {
                IssuingCA = Certificate.Issuer.Split('=')[1];
                Valid = false;
                return false;
            }
        }

        public bool ValidateChain(X509Certificate2Collection publicCert)
        {
            try
            {
                X509Chain ch = new X509Chain(true);
                ch.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                ch.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                foreach (X509Certificate2 xPub in publicCert)
                {
                    ch.ChainPolicy.ExtraStore.Add(xPub);
                }

                bool bChain = ch.Build(Certificate);
                Chain = ch;
                Status = ch.ChainStatus;
                try
                {
                    IssuingCA = ch.ChainElements[1].Certificate.Subject.Split('=')[1];
                    RootCA = ch.ChainElements[2].Certificate.Subject.Split('=')[1];
                }
                catch { }

                Valid = bChain;
                return bChain;
            }
            catch
            {
                Valid = false;
                return false;
            }
        }

        public X509Chain Chain { get; set; }
        public X509ChainStatus[] Status { get; set; }

        public string IssuingCA { get; set; }

        public string RootCA { get; set; }

        public bool HasPrivateKey { get; set; }

        public bool Expired { get; set; }

        public bool Exists { get; set; }

        public bool Valid { get; set; }

        public string EndpointURL
        {
            get
            {
                if (Exists && Valid)
                {
                    if (!string.IsNullOrEmpty(IssuingCA))
                    {
                        return $"https://{Chain.ChainElements[1].Certificate.GetNameInfo(X509NameType.DnsFromAlternativeName, false)}/chat";
                    }
                }

                return "";
            }
        }

        public string FallbackURL
        {
            get
            {
                if (Exists && Valid)
                {
                    if (!string.IsNullOrEmpty(RootCA))
                    {
                        return $"https://{Chain.ChainElements[2].Certificate.GetNameInfo(X509NameType.DnsFromAlternativeName, false)}/chat";
                    }
                }

                return "";
            }
        }

        public string DeviceID { get; set; }

        public string CustomerID { get; set; }

        public string Signature
        {
            get
            {
                try
                {
                    string sig = "";
                    if (Exists && Valid)
                    {
                        if (Certificate.GetKeyAlgorithm() == "1.2.840.10045.2.1") //ECDsa Key
                        {
                            using (ECDsa key = Certificate.GetECDsaPrivateKey())
                            {
                                if (key == null)
                                {
                                    throw new Exception("No valid cert was found");
                                }

                                sig = Convert.ToBase64String(key.SignData(Encoding.Default.GetBytes(DeviceID), HashAlgorithmName.SHA256));
                            }
                        }
                        else
                        {
                            using (RSA key = Certificate.GetRSAPrivateKey())
                            {
                                if (key == null)
                                {
                                    throw new Exception("No valid cert was found");
                                }

                                sig = Convert.ToBase64String(key.SignData(Encoding.Default.GetBytes(DeviceID), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                            }
                        }

                        //create JSON
                        JObject jObj = new JObject();
                        jObj.Add(new JProperty("MSG", DeviceID));
                        jObj.Add(new JProperty("SIG", sig));
                        jObj.Add(new JProperty("CER", Convert.ToBase64String(Certificate.Export(X509ContentType.Cert)))); //Public Key

                        //create Base64 string
                        return Convert.ToBase64String(Encoding.Default.GetBytes(jObj.ToString(Newtonsoft.Json.Formatting.None)));
                    }
                }
                catch (Exception ex)
                {
                    ex.Message.ToString();
                }

                return "";
            }
        }
    }
}
