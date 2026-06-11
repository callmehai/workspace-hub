namespace WorkspaceHub.Application.Security
{
    public interface ITokenProtector
    {
        //encrypt string plain token to protect string token 
        string Protect(string plainText);

        //Decrypt string token to use for call API 
        string Unprotect(string plainText);
    }
}
