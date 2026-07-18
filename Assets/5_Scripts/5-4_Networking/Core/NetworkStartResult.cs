namespace FlatWorld.Networking
{
    public readonly struct NetworkStartResult
    {
        public bool Success { get; }
        public string Error { get; }

        private NetworkStartResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public static NetworkStartResult Started()
        {
            return new NetworkStartResult(true, string.Empty);
        }

        public static NetworkStartResult Failed(string error)
        {
            return new NetworkStartResult(false, error ?? string.Empty);
        }
    }
}
