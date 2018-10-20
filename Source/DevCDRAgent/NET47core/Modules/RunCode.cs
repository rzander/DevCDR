//
//Based on Tim MalcomVetter .NET Process Injection 
//https://medium.com/@malcomvetter/net-process-injection-1a1af00359bc
//https://github.com/malcomvetter/ManagedInjection
//

using System;
using System.Reflection;
using System.Security.Cryptography;

namespace DevCDRAgent.Modules
{
    public static class ManagedInjection
    {
        //Run an assembly from a Base64 string
        public static void Inject(string assemblyB64)
        {
            var bytes = Convert.FromBase64String(assemblyB64);
            var assembly = Assembly.Load(bytes);

            //Assembly must be signed
            if (assembly.GetName().GetPublicKeyToken().Length > 0)
            {
                string hash1;
                string hash2;

                //get hash of assemly signatures
                using (SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
                {
                    hash1 = Convert.ToBase64String(sha1.ComputeHash(assembly.GetName().GetPublicKeyToken()));
                    hash2 = Convert.ToBase64String(sha1.ComputeHash(Assembly.GetExecutingAssembly().GetName().GetPublicKeyToken()));
                }

                //Only allow assembly with same signature
                if (hash1 == hash2)
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        object instance = Activator.CreateInstance(type);
                        object[] args = new object[] { new string[] { "" } };
                        try
                        {
                            type.GetMethod("Main").Invoke(instance, args);
                        }
                        catch { }
                    }
                }
            }
        }
    }
}
