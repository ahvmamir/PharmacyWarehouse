namespace PharmacyWarehouse.Client.Auth;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
    NetworkError,
    UnexpectedError
}