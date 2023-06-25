﻿using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Celery.Controls
{
    public enum AnnouncementType
    {
        Standard = 0,
        Update = 1
    }

    public partial class AnnouncementBox : UserControl
    {
        public AnnouncementBox(string markdown, AnnouncementType type)
        {
            InitializeComponent();
            MarkdownBox.Markdown = markdown;
            MarkdownBox.MarkdownStyleName = "SasabuneCompact";
            switch (type)
            {
                case AnnouncementType.Update:
                    AnnouncementIcon.StrokeThickness = 0;
                    AnnouncementIcon.Fill = (Brush)Application.Current.Resources["ForegroundBrush"];
                    AnnouncementIcon.Width = 20;
                    AnnouncementIcon.Data = Geometry.Parse("M21,24H11a2,2,0,0,0-2,2v2a2,2,0,0,0,2,2H21a2,2,0,0,0,2-2V26A2,2,0,0,0,21,24Zm0,4H11V26H21Z M28.707,14.293l-12-12a.9994.9994,0,0,0-1.414,0l-12,12A1,1,0,0,0,4,16H9v4a2.0023,2.0023,0,0,0,2,2H21a2.0027,2.0027,0,0,0,2-2V16h5a1,1,0,0,0,.707-1.707ZM21,14v6H11V14H6.4141L16,4.4141,25.5859,14Z");
                    break;
                case AnnouncementType.Standard:
                    AnnouncementIcon.StrokeThickness = 1.5;
                    AnnouncementIcon.Fill = null;
                    AnnouncementIcon.Width = 25;
                    AnnouncementIcon.Data = Geometry.Parse("M22 7.99992V11.9999M10.25 5.49991H6.8C5.11984 5.49991 4.27976 5.49991 3.63803 5.82689C3.07354 6.11451 2.6146 6.57345 2.32698 7.13794C2 7.77968 2 8.61976 2 10.2999L2 11.4999C2 12.4318 2 12.8977 2.15224 13.2653C2.35523 13.7553 2.74458 14.1447 3.23463 14.3477C3.60218 14.4999 4.06812 14.4999 5 14.4999V18.7499C5 18.9821 5 19.0982 5.00963 19.1959C5.10316 20.1455 5.85441 20.8968 6.80397 20.9903C6.90175 20.9999 7.01783 20.9999 7.25 20.9999C7.48217 20.9999 7.59826 20.9999 7.69604 20.9903C8.64559 20.8968 9.39685 20.1455 9.49037 19.1959C9.5 19.0982 9.5 18.9821 9.5 18.7499V14.4999H10.25C12.0164 14.4999 14.1772 15.4468 15.8443 16.3556C16.8168 16.8857 17.3031 17.1508 17.6216 17.1118C17.9169 17.0756 18.1402 16.943 18.3133 16.701C18.5 16.4401 18.5 15.9179 18.5 14.8736V5.1262C18.5 4.08191 18.5 3.55976 18.3133 3.2988C18.1402 3.05681 17.9169 2.92421 17.6216 2.88804C17.3031 2.84903 16.8168 3.11411 15.8443 3.64427C14.1772 4.55302 12.0164 5.49991 10.25 5.49991Z");
                    break;
            }
        }

        private void MarkdownBox_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            StackPanel parent = (StackPanel)Parent;
            ScrollViewer scrollViewer = (ScrollViewer)parent.Parent;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta / 3));
        }
    }
}
