using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace DevCDRAgent.Modules
{
    public class SignatureVerification
    {
         internal static bool addCertToStore(System.Security.Cryptography.X509Certificates.X509Certificate2 cert, System.Security.Cryptography.X509Certificates.StoreName st, System.Security.Cryptography.X509Certificates.StoreLocation sl)
        {
            bool bRet = false;

            try
            {
                X509Store store = new X509Store(st, sl);
                store.Open(OpenFlags.ReadWrite);
                var certs = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, cert.SubjectName.Name, false);

                //Remove existing Certificates
                foreach (X509Certificate2 cer in certs)
                {
                    store.Remove(cer);
                }

                store.Add(cert);

                store.Close();
            }
            catch
            {

            }

            return bRet;
        }
    }

    public class X509AgentCert
    {
        public static X509Certificate2Collection publicCertificates = new X509Certificate2Collection();
        public X509AgentCert(string deviceID)
        {
            DeviceID = deviceID;
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

        public X509AgentCert(string deviceID, string signature)
        {
            DeviceID = deviceID;
            Exists = false;
            Expired = false;
            Valid = false;
            HasPrivateKey = false;

            try
            {
                JObject jObj = JObject.Parse(Encoding.Default.GetString(Convert.FromBase64String(signature)));
                if (deviceID == jObj["MSG"].Value<string>())
                {
                    Certificate = new X509Certificate2(Convert.FromBase64String(jObj["CER"].Value<string>()));
                    Exists = true;
                    IssuingCA = Certificate.Issuer.Split('=')[1];
                    if (publicCertificates.Count > 0)
                        ValidateChain(publicCertificates);
                    else
                        ValidateChain();
                    HasPrivateKey = Certificate.HasPrivateKey;
                    Signature.ToString(); //generate Signature

                    if (Certificate.NotAfter < DateTime.UtcNow)
                    {
                        Expired = true;
                    }
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
                foreach (X509Certificate2 xPub in publicCertificates)
                {
                    ch.ChainPolicy.ExtraStore.Add(xPub);
                }

                bool bChain = ch.Build(Certificate);
                Chain = ch;
                Status = ch.ChainStatus;
                IssuingCA = ch.ChainElements[1].Certificate.Subject.Split('=')[1];
                RootCA = ch.ChainElements[2].Certificate.Subject.Split('=')[1];
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
