using Android.Bluetooth.LE;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using App4.Services;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using MauiCameraViewSample.Platforms.Android;
using System.Diagnostics;
using Xamarin.Essentials;
using static Android.Provider.CalendarContract;

namespace AndroidApp3
{
    [Activity(Label = "@string/app_name", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : Android.App.Activity
    {
        readonly GoogleDriveService _googleDriveService = new();
        private Camera2Implementation _camera2Implementation;
        private Button _Sign_In;
        private Button _Start_Stream;

        protected override async void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);


            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            _Sign_In = FindViewById<Button>(Resource.Id.Sign_In);
            _Start_Stream = FindViewById<Button>(Resource.Id.Start_Stream);

            _Sign_In.Click += SignIn_Clicked;
            _Start_Stream.Click += Create_Stream_Button;
            TextureView textureView = FindViewById<TextureView>(Resource.Id.Texture_View1);
            _camera2Implementation= new Camera2Implementation(textureView);
            await ContentPage_Loaded();
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        public async Task CreateStream()
        {
            try
            {
                var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = _googleDriveService._credential,
                    ApplicationName = Constants.Package_Name
                });

                // Создаем трансляцию
                var broadcast = new LiveBroadcast
                {
                    ContentDetails = new() { EnableAutoStart = true },
                    Snippet = new LiveBroadcastSnippet
                    {
                        Title = $"Test Stream - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        ScheduledStartTimeDateTimeOffset = DateTimeOffset.UtcNow.AddMinutes(1), // Запланированное время
                    },
                    Status = new LiveBroadcastStatus
                    {
                        PrivacyStatus = "public" // Приватность: public, unlisted или private
                    }
                };
                var broadcastRequest = youtubeService.LiveBroadcasts.Insert(broadcast, "snippet,status,contentDetails");
                var broadcastResponse = await broadcastRequest.ExecuteAsync();

                // Создаем поток
                var stream = new LiveStream
                {
                    Snippet = new LiveStreamSnippet
                    {
                        Title = $"Test Stream - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    },
                    Cdn = new CdnSettings
                    {
                        FrameRate = "30fps",
                        Resolution = "1080p",
                        Format = "1080p",
                        IngestionType = "rtmp" // Тип RTMP
                    }
                };
                var streamRequest = youtubeService.LiveStreams.Insert(stream, "snippet,cdn");
                var streamResponse = await streamRequest.ExecuteAsync();

                // Связываем трансляцию и поток
                var bindRequest = youtubeService.LiveBroadcasts.Bind(broadcastResponse.Id, "id,contentDetails");
                bindRequest.StreamId = streamResponse.Id;
                //////////////////////////////////////////////////////////////////
                ///На этом этапе уже можно увидеть созданную запланированную трансляцию на ютубе
                var bindResponse = await bindRequest.ExecuteAsync();

                // Логирование результатов
                Debug.WriteLine("Stream and Broadcast created successfully.");
                Debug.WriteLine($"Stream ID: {streamResponse.Id}");
                Debug.WriteLine($"Broadcast ID: {broadcastResponse.Id}");
                Debug.WriteLine($"Ingestion Address: {streamResponse.Cdn.IngestionInfo.IngestionAddress}");
                Debug.WriteLine($"Stream Key: {streamResponse.Cdn.IngestionInfo.StreamName}");

                // Формирование URL-адреса для стрима
                string streamUrl = $"{streamResponse.Cdn.IngestionInfo.IngestionAddress}/{streamResponse.Cdn.IngestionInfo.StreamName}";
                // Debug.WriteLine($"Stream URL: {streamUrl}");

                // Можно передать streamUrl в метод стриминга
                //await Task.Run(() => StartStreaming(streamUrl));
                var status = await Permissions.RequestAsync<Permissions.Camera>();
                if(status != PermissionStatus.Granted)
                {
                    return;
                }
                await Task.Run(async () => {
                    _camera2Implementation.StartCamera();
                    await _camera2Implementation.StartStream(streamUrl);

                });

            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error creating stream: {ex.Message}");
            }
        }

        private async Task ContentPage_Loaded()
        {
            await _googleDriveService.Init();
            UpdateButton();
        }
        private async void SignIn_Clicked(object sender, EventArgs e)
        {
            if (_Sign_In.Text == "Sign In")
            {
                await _googleDriveService.SignIn();
                //SignInButton.BackgroundColor = Colors.Green;

            }
            else
            {
                await _googleDriveService.SignOut();
                //SignInButton.BackgroundColor = Colors.Gray;

            }
            UpdateButton();
        }



        private void UpdateButton()
        {
            if (_googleDriveService.IsSignedIn)
            {
                _Sign_In.Text = $"Sign Out ({_googleDriveService.Email})";
                //_Sign_In.BackgroundColor = Colors.Green;

                //ListButton.IsVisible = true;
            }
            else
            {
                //SignInButton.BackgroundColor = Colors.Gray;

                _Sign_In.Text = "Sign In";

                //ListButton.IsVisible = false;
                //ListLabel.Text = String.Empty;
            }
        }

        private async void Create_Stream_Button(object sender, EventArgs e)
        {
            if (_googleDriveService.IsSignedIn)
            {
                await CreateStream();
                //await StartStreaming();
            }
        }



    }
}