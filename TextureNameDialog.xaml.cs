using System.Text.RegularExpressions;
using System.Windows;

namespace FooxClothStudio;

public partial class TextureNameDialog : Window
{
    public string TextureName => NameBox.Text.Trim();

    public TextureNameDialog(string suggestedName, bool renaming = false)
    {
        InitializeComponent();
        if (renaming)
        {
            Title = "Renomear textura";
            PromptText.Text = "Novo nome da textura dentro do YTD";
            AcceptButton.Content = "Renomear";
        }
        NameBox.Text = suggestedName;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (!Regex.IsMatch(TextureName, @"^[A-Za-z0-9_]{1,64}$"))
        {
            ErrorText.Text = "Use de 1 a 64 letras, números ou _.";
            return;
        }
        DialogResult = true;
    }
}
