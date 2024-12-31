using Android.Content;
using Android.Hardware.Camera2;
using Android.Views;
using Android.Util;
using Java.Nio;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Android.App;
using Google.Apis.Auth.OAuth2;
using Android.OS;
using Android.Graphics;
using Laerdal.FFmpeg;


namespace MauiCameraViewSample.Platforms.Android
{
    internal class Camera2Implementation
    {
        private CameraDevice _cameraDevice;
        private CameraCaptureSession _cameraSession;
        private CaptureRequest.Builder _previewBuilder;
        private TextureView _textureView;
        private string _cameraId;
        private CameraManager _cameraManager;
        private bool _isCameraOpen = false;

        private System.Diagnostics.Process ffmpegProcess;
        


        public Camera2Implementation(TextureView textureView)
        {
            _textureView = textureView;
            _cameraManager = (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService);
        }

        // Запуск камеры
        public void StartCamera()
        {
            try
            {
                if (_textureView.IsAvailable)
                {
                    InitializeCamera(_textureView.SurfaceTexture);
                }
                else
                {
                    // Устанавливаем слушатель, чтобы дождаться готовности SurfaceTexture
                    _textureView.SurfaceTextureListener = new CameraSurfaceTextureListener
                    {
                        OnSurfaceTextureAvailableAction = (surface, width, height) =>
                        {
                            InitializeCamera(surface);
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Error("Camera2Implementation", $"Ошибка при запуске камеры: {ex.Message}");
            }
        }

        private void InitializeCamera(SurfaceTexture surface)
        {
            try
            {
                string[] cameraIdList = _cameraManager.GetCameraIdList();
                _cameraId = cameraIdList.FirstOrDefault(); // Берем первый доступный ID камеры

                if (_cameraId == null)
                {
                    Log.Error("Camera2Implementation", "Камера не найдена.");
                    return;
                }

                var cameraStateCallback = new CameraStateCallback
                {
                    OnOpenedAction = cameraDevice =>
                    {
                        var _surface = new Surface(surface);
                        _cameraDevice = cameraDevice;
                        _isCameraOpen = true;

                        // Создаем запрос на захват
                        _previewBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                        _previewBuilder.AddTarget(_surface);

                        // Настроим сессию захвата
                        _cameraDevice.CreateCaptureSession(
                            new List<Surface> { _surface },
                            new CameraCaptureStateCallback
                            {
                                OnConfiguredAction = session =>
                                {
                                    _cameraSession = session;
                                    _cameraSession.SetRepeatingRequest(_previewBuilder.Build(), null, null);
                                }
                            },
                            null
                        );
                    }
                };

                // Открываем камеру в основном потоке
                var mainHandler = new Handler(Looper.MainLooper);
                mainHandler.Post(() =>
                {
                    _cameraManager.OpenCamera(_cameraId, cameraStateCallback, null);
                });
            }
            catch (Exception ex)
            {
                Log.Error("Camera2Implementation", $"Ошибка при запуске камеры: {ex.Message}");
            }
        }

        // Реализация SurfaceTextureListener
        private class CameraSurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
        {
            public Action<SurfaceTexture, int, int> OnSurfaceTextureAvailableAction { get; set; }

            public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
            {
                OnSurfaceTextureAvailableAction?.Invoke(surface, width, height);
            }

            public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) => true;

            public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) { }

            public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }
        }


        // Остановка камеры
        public void StopCamera()
        {
            if (_cameraSession != null)
            {
                _cameraSession.Close();
                _cameraSession = null;
            }

            if (_cameraDevice != null)
            {
                _cameraDevice.Close();
                _cameraDevice = null;
            }

            _isCameraOpen = false;
        }

        // Захват кадров с камеры и отправка их в FFmpeg
        private async Task CaptureAndSendFrameAsync()
        {
            try
            {
                var buffer = ByteBuffer.AllocateDirect(1920 * 1080 * 3 / 2); // Размер буфера для захвата видео
                var surfaceTexture = _textureView.SurfaceTexture;

                if (surfaceTexture == null)
                {
                    Log.Error("Camera2Implementation", "Текстурный поверхностный объект отсутствует.");
                    return;
                }

                var surface = new Surface(surfaceTexture);

                // Получаем захваченные кадры и передаем их в FFmpeg
                while (_isCameraOpen)
                {
                    // Получаем изображение с камеры в формате YUV
                    var image = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview).Build();

                    // Передаем кадр через FFmpeg
                    if (ffmpegProcess != null && ffmpegProcess.StartInfo != null)
                    {
                        // Отправляем данные на вход FFmpeg
                        var byteArray = new byte[buffer.Capacity()];
                        buffer.Get(byteArray);
                        await WriteFrameToFFmpeg(byteArray);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Camera2Implementation", $"Ошибка при захвате кадра: {ex.Message}");
            }
        }

        // Метод для записи кадра в FFmpeg
        private async Task WriteFrameToFFmpeg(byte[] frameData)
        {
            if (ffmpegProcess == null || ffmpegProcess.HasExited)
            {
                return;
            }

            using (var stream = ffmpegProcess.StandardInput.BaseStream)
            {
                await stream.WriteAsync(frameData, 0, frameData.Length);
            }
        }

        // Старт стрима
        public async Task StartStream(string streamUrl)
        {
            if (_isCameraOpen)
            {
                // Настройка аргументов для FFmpeg
                //string streamUrl = "rtmp://your-stream-url";
                string ffmpegArgs = $"-f rawvideo -pix_fmt yuv420p -s 1920x1080 -r 30 -i pipe:0 " +
                                    $"-vcodec libx264 -b:v 1000k -maxrate 1000k -bufsize 2000k -acodec aac -b:a 128k -f flv {streamUrl}";

                try
                {
                    ffmpegProcess = new System.Diagnostics.Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = ffmpegArgs,
                            RedirectStandardInput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    ffmpegProcess.Start();
                    await CaptureAndSendFrameAsync();  // Начинаем захват и передачу данных в FFmpeg
                }
                catch (Exception ex)
                {
                    Log.Error("Camera2Implementation", $"Ошибка при запуске FFmpeg: {ex.Message}");
                }
            }
        }

        // Метод для остановки стрима
        public void StopStream()
        {
            if (ffmpegProcess != null && !ffmpegProcess.HasExited)
            {
                ffmpegProcess.Kill();
                ffmpegProcess = null;
            }
        }

        // Ожидание потока с камеры
        private class CameraStateCallback : CameraDevice.StateCallback
        {
            public Action<CameraDevice> OnOpenedAction { get; set; }

            public override void OnOpened(CameraDevice camera)
            {
                OnOpenedAction?.Invoke(camera);
            }

            public override void OnDisconnected(CameraDevice camera)
            {
                camera.Close();
            }

            public override void OnError(CameraDevice camera, CameraError error)
            {
                camera.Close();
            }
        }

        // Обработчик состояния захвата
        private class CameraCaptureStateCallback : CameraCaptureSession.StateCallback
        {
            public Action<CameraCaptureSession> OnConfiguredAction { get; set; }

            public override void OnConfigured(CameraCaptureSession session)
            {
                OnConfiguredAction?.Invoke(session);
            }

            public override void OnConfigureFailed(CameraCaptureSession session)
            {
            }
        }
    }
}
