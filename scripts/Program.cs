using Merkdito.Api.Security;
using System.Text;
// Use only in a local development terminal; does not write the raw password to disk.
Console.Write("Senha do administrador de laboratório: ");
var buffer=new StringBuilder();
while (true) {
    var key=Console.ReadKey(intercept:true);
    if(key.Key==ConsoleKey.Enter)break;
    if(key.Key==ConsoleKey.Backspace){if(buffer.Length>0)buffer.Length--;continue;}
    if(!char.IsControl(key.KeyChar))buffer.Append(key.KeyChar);
}
Console.WriteLine();
if(buffer.Length<12)throw new InvalidOperationException("Informe pelo menos 12 caracteres.");
Console.WriteLine(PasswordHashing.Hash(buffer.ToString()));
buffer.Clear();
