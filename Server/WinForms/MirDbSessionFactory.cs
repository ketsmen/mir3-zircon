using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using System;
using System.Reflection;

namespace Server.WinForms
{
    public static class MirDbSessionFactory
    {
        public static Assembly[] DatabaseAssemblies { get; } =
        {
            Assembly.GetAssembly(typeof(ItemInfo)),
            Assembly.GetAssembly(typeof(AccountInfo))
        };

        public static void ConfigureEncryption()
        {
            try
            {
                if (!string.IsNullOrEmpty(Config.EncryptionKey))
                    SEnvir.CryptoKey = Convert.FromBase64String(Config.EncryptionKey);
            }
            catch (Exception)
            {
                throw new ApplicationException("加密密钥格式无效，应为 32 字节的 Base64 字符串。");
            }

            if (Config.EncryptionEnabled && SEnvir.CryptoKey == null)
                throw new ApplicationException("已启用数据库加密，但未指定 [System] => DatabaseKey。");

            Encryption.SetKey(Config.EncryptionEnabled ? SEnvir.CryptoKey : null);
        }

        public static Session Open(SessionMode mode)
        {
            ConfigureEncryption();

            var session = new Session(mode)
            {
                BackUpDelay = 60
            };

            session.Initialize(DatabaseAssemblies);
            return session;
        }
    }
}
