namespace TunnelFlow.Bootstrapper;

internal enum BootstrapperExitCode
{
    Success = 0,
    UnknownError = 1,
    InvalidArguments = 2,
    ServiceBinaryNotFound = 3,
    AlreadyInstalled = 4,
    NotInstalled = 5,
    Timeout = 6,
    UserCanceled = 7,
    AccessDenied = 8,
    NotImplemented = 9
}
