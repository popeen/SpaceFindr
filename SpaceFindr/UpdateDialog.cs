using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Documents;

namespace SpaceFindr
{
    public class UpdateDialog : Window
    {
        public UpdateDialog(string version, string url)
        {
            Title = "Update Available";
            SizeToContent = SizeToContent.Height;
            Width = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock
            {
                Text = $"A new version ({version}) is available!",
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Margin = new Thickness(0,0,0,12)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "See details:",
                Margin = new Thickness(0,0,0,4)
            });
            var link = new TextBlock();
            var hyperlink = new Hyperlink { NavigateUri = new Uri(url) };
            hyperlink.Inlines.Add(url);
            hyperlink.RequestNavigate += (s, e) =>
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            };
            link.Inlines.Add(hyperlink);
            stack.Children.Add(link);
            var btn = new Button
            {
                Content = "Go to update page",
                Margin = new Thickness(0,16,0,0),
                Padding = new Thickness(12,4,12,4),
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 120
            };
            btn.Click += (s, e) =>
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Close();
            };
            stack.Children.Add(btn);
            Content = stack;
        }
    }
}
