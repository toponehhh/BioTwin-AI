namespace BioTwin_AI.BlazorClient.Services;

public sealed class AuthModalState
{
    public bool IsOpen { get; private set; }

    public bool IsRegisterMode { get; private set; }

    public event Action? Changed;

    public void OpenSignIn()
    {
        IsRegisterMode = false;
        IsOpen = true;
        Changed?.Invoke();
    }

    public void OpenRegister()
    {
        IsRegisterMode = true;
        IsOpen = true;
        Changed?.Invoke();
    }

    public void UseSignIn()
    {
        IsRegisterMode = false;
        Changed?.Invoke();
    }

    public void UseRegister()
    {
        IsRegisterMode = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Changed?.Invoke();
    }
}
