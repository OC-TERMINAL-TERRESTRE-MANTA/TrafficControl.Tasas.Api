using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ParkingControl.Facturacion.Worker.Criptografia
{
    public static class clsCriptografiaModerna
    {
        // 🔐 Recomendación: cambia esta clave y protégela
        private const string FraseSecreta = "OC-MASTERKEY";

        public static string Encriptar(string textoPlano)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] iv;
            byte[] encriptado;

            using var aes = Aes.Create();
            using var pbkdf2 = new Rfc2898DeriveBytes(FraseSecreta, salt, 100_000, HashAlgorithmName.SHA256);

            aes.Key = pbkdf2.GetBytes(32);
            aes.GenerateIV();
            iv = aes.IV;

            using var encryptor = aes.CreateEncryptor();
            byte[] datos = Encoding.UTF8.GetBytes(textoPlano);
            encriptado = encryptor.TransformFinalBlock(datos, 0, datos.Length);

            // Combina: [salt][IV][data]
            using var ms = new MemoryStream();
            ms.Write(salt, 0, salt.Length);
            ms.Write(iv, 0, iv.Length);
            ms.Write(encriptado, 0, encriptado.Length);

            return Convert.ToBase64String(ms.ToArray());
        }

        public static string Desencriptar(string textoEncriptado)
        {
            byte[] datos = Convert.FromBase64String(textoEncriptado);

            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            byte[] encriptado = new byte[datos.Length - 32];

            Array.Copy(datos, 0, salt, 0, 16);
            Array.Copy(datos, 16, iv, 0, 16);
            Array.Copy(datos, 32, encriptado, 0, encriptado.Length);

            using var pbkdf2 = new Rfc2898DeriveBytes(FraseSecreta, salt, 100_000, HashAlgorithmName.SHA256);
            using var aes = Aes.Create();
            aes.Key = pbkdf2.GetBytes(32);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            byte[] resultado = decryptor.TransformFinalBlock(encriptado, 0, encriptado.Length);

            return Encoding.UTF8.GetString(resultado);
        }
    }
}
