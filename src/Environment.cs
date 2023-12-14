namespace Aire.Memory
{
    public static class AireEnvironment
    {
        public static string? DatabaseConnectionString => Environment.GetEnvironmentVariable("DatabaseConnectionString");

        public static string? TokenIssuer => Environment.GetEnvironmentVariable("TokenIssuer");
        public static string? TokenAudience => Environment.GetEnvironmentVariable("TokenAudience");
        public static string? TokenSigningKey => Environment.GetEnvironmentVariable("TokenSigningKey");
        public static string? TokenEncryptionKey = Environment.GetEnvironmentVariable("TokenEncryptionKey");
    }
}
