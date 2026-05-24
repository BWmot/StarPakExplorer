using System.Text;

namespace StarPakExplorer.UI.ViewModels;

public sealed class TextEncodingChoice
{
    public TextEncodingChoice(string name, Encoding encoding)
    {
        Name = name;
        Encoding = encoding;
    }

    public string Name { get; }

    public Encoding Encoding { get; }
}
