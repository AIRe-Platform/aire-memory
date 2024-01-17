namespace Aire.Memory
{
    public static class AireEnvironment
    {
        public static string? DatabaseConnectionString => Environment.GetEnvironmentVariable("DatabaseConnectionString");
        public static string? TokenSigningKey => Environment.GetEnvironmentVariable("TokenSigningKey");
        public static string? TokenEncryptionKey => Environment.GetEnvironmentVariable("TokenEncryptionKey");
        public static string? OpenApiHost => Environment.GetEnvironmentVariable("OpenApi__HostNames");
    }
}
