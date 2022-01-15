using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Biscord
{
    /// <summary>
    /// Interaction logic for TokenInputDialogue.xaml
    /// </summary>
    public partial class TokenInputDialogue : Window
    {
        public static string? token { get; set; }

        public TokenInputDialogue()
        {
            InitializeComponent();
        }

        private void btnDialogOk_Click(object sender, RoutedEventArgs e)
        {
            if (tokenAnswer.Text == "")
            {
                DialogResult = false;
            }
            else
            {
                DialogResult = true;
                token = tokenAnswer.Text;
            }
        }
    }
}
