namespace XianAuth
{
    public enum AuthStatus
    {
        Success,
        AlreadyOnline,
        CredentialRejected,
        Rejected,
        Transient,
        Skipped,
        Locked,
        NotTargetNetwork,
        Offline
    }

    public sealed class AuthOutcome
    {
        public AuthOutcome(AuthStatus status, string message)
        {
            Status = status;
            Message = message ?? "";
        }

        public AuthStatus Status { get; private set; }
        public string Message { get; private set; }

        public bool IsOnline
        {
            get { return Status == AuthStatus.Success || Status == AuthStatus.AlreadyOnline; }
        }

        public bool ShouldRetry
        {
            get
            {
                return Status == AuthStatus.Transient
                    || Status == AuthStatus.NotTargetNetwork
                    || Status == AuthStatus.Offline;
            }
        }

        public static AuthOutcome Success(string message)
        {
            return new AuthOutcome(AuthStatus.Success, message);
        }

        public static AuthOutcome AlreadyOnline(string message)
        {
            return new AuthOutcome(AuthStatus.AlreadyOnline, message);
        }

        public static AuthOutcome CredentialRejected(string message)
        {
            return new AuthOutcome(AuthStatus.CredentialRejected, message);
        }

        public static AuthOutcome Rejected(string message)
        {
            return new AuthOutcome(AuthStatus.Rejected, message);
        }

        public static AuthOutcome Transient(string message)
        {
            return new AuthOutcome(AuthStatus.Transient, message);
        }

        public static AuthOutcome NotTarget(string message)
        {
            return new AuthOutcome(AuthStatus.NotTargetNetwork, message);
        }
    }

    public sealed class AuthRunResult
    {
        public AuthRunResult(AuthStatus status, string message)
        {
            Status = status;
            Message = message ?? "";
        }

        public AuthStatus Status { get; private set; }
        public string Message { get; private set; }

        public int ExitCode
        {
            get
            {
                switch (Status)
                {
                    case AuthStatus.Success:
                    case AuthStatus.AlreadyOnline:
                    case AuthStatus.Skipped:
                        return 0;
                    case AuthStatus.CredentialRejected:
                        return 2;
                    case AuthStatus.Locked:
                        return 3;
                    case AuthStatus.NotTargetNetwork:
                        return 4;
                    default:
                        return 1;
                }
            }
        }

        public static AuthRunResult Skipped(string message)
        {
            return new AuthRunResult(AuthStatus.Skipped, message);
        }

        public static AuthRunResult From(AuthOutcome outcome)
        {
            if (outcome == null)
                return new AuthRunResult(AuthStatus.Transient, "无结果");
            return new AuthRunResult(outcome.Status, outcome.Message);
        }
    }
}
