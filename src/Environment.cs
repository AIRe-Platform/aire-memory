namespace Aire.Memory
{
    public static class AireEnvironment
    {
        public static string? DatabaseConnectionString => Environment.GetEnvironmentVariable("DatabaseConnectionString");
        public static string? StorageConnectionString => Environment.GetEnvironmentVariable("StorageConnectionString");
        public static string? TokenSigningKey => Environment.GetEnvironmentVariable("TOKEN_SIGNING_KEY");
        public static string? TokenEncryptionKey => Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY");
        public static string? PlatformServiceKey => Environment.GetEnvironmentVariable("AIRE_SERVICE_KEY");
        public static string? PlatformServiceUrl => Environment.GetEnvironmentVariable("AIRE_SERVICE_BASE");
        public static string? OpenApiHost => Environment.GetEnvironmentVariable("OpenApi__HostNames");
    }
}
