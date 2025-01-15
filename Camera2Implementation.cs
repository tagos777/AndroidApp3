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
using Android.Media;
using Laerdal.FFmpeg.Android;
using Java.IO;
using Java.Interop;
using LibVLCSharp.Shared;
using Android.Media.TV;


namespace MauiCameraViewSample.Platforms.Android
{
    internal class Camera2Implementation
    {
        private LibVLC _libVlc;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;

        private CameraDevice _cameraDevice;
        private CameraCaptureSession _cameraSession;
        private CaptureRequest.Builder _previewBuilder;
        private TextureView _textureView;
        private string _cameraId;
        private CameraManager _cameraManager;
        private bool _isCameraOpen = false;

        private System.Diagnostics.Process ffmpegProcess;
        private Object _lock=new();

        private ImageReader _imageReader;
        private ImageAvailableListener _imageListener;

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
                        lock (_lock)
                        {
                            _isCameraOpen = true;

                        }
                        SetupImageReader();
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
                                    _previewBuilder.AddTarget(_imageReader.Surface); // Добавляем ImageReader как целевой объект
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
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Настройка ImageReader
        private void SetupImageReader()
        {
            // Устанавливаем размер кадров и формат
            _imageReader = ImageReader.NewInstance(1920, 1080, ImageFormatType.Yuv420888, 2);
            _imageReader.SetOnImageAvailableListener(new ImageAvailableListener
            {
                ImageAvailable = imageReader =>
                {
                    // Обработка кадра
                    using (var image = imageReader.AcquireLatestImage())
                    {
                        if (image != null)
                        {
                            ProcessImage(image);
                            image.Close();
                        }
                    }
                }
            }, null);
        }
        public class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
        {
            public Action<ImageReader> ImageAvailable;

            public void OnImageAvailable(ImageReader reader)
            {
                // Trigger the event when a new image is available
                ImageAvailable?.Invoke(reader);
            }
        }
        public void InitializeVlcStream(string rtmpUrl)
        {
            Core.Initialize(); // Инициализация LibVLC
            _libVlc = new LibVLC();

            // Настройка MediaPlayer
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVlc);

            var media = new Media(_libVlc, $"sout=#transcode{{vcodec=h264,vb=1000,acodec=none}}:rtp{{dst={rtmpUrl},port=1234,mux=ts}}", FromType.FromLocation);
            _mediaPlayer.Play(media);
        }
        private void SendFrameToVlc(byte[] data)
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    Log.Error("SendFrameToVlc", "MediaPlayer не инициализирован.");
                    return;
                }

                // Создаем MemoryStream из переданных данных
                using (var stream = new MemoryStream(data))
                {
                    
                    // Используем API VLC для передачи данных в поток
                    var mediaInput = new CustomMediaInput(stream);

                    // Привязываем поток к MediaPlayer
                    _mediaPlayer.Media = new Media(_libVlc, mediaInput);

                    // Проверяем, играет ли MediaPlayer
                    if (!_mediaPlayer.IsPlaying)
                    {
                        _mediaPlayer.Play();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("SendFrameToVlc", $"Ошибка отправки кадра в VLC: {ex.Message}");
            }
        }

        // Обработка изображения
        private void ProcessImage(Image image)
        {
            try
            {
                // Пример обработки кадра: преобразуем YUV в ByteArray
                var planes = image.GetPlanes();
                var buffer = planes[0].Buffer;
                var data = new byte[buffer.Remaining()];
                buffer.Get(data);

                // Отправляем кадр в поток для FFmpeg
                // SendFrameToFFmpeg(data);

                // Отправляем кадры в VLC
                SendFrameToVlc(data);
            }
            catch (Exception ex)
            {
                Log.Error("Camera2Implementation", $"Ошибка обработки изображения: {ex.Message}");
            }
        }
        private void SendFrameToFFmpeg(byte[] data)
        {
            // Убедитесь, что путь к FIFO корректный
            string fifoPath = "/data/data/com.tagos.oauthsample/files/input_fifo";

            try
            {
                // Открываем FIFO для записи
                using (var fifoStream = new FileStream(fifoPath, FileMode.Open, FileAccess.Write))
                {
                    // Пишем данные в FIFO
                    fifoStream.Write(data, 0, data.Length);
                    fifoStream.Flush(); // Убедитесь, что данные отправлены
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибки для отладки
                System.Diagnostics.Debug.WriteLine($"Ошибка при отправке кадра в FFmpeg: {ex.Message}");
            }
        }

        public async Task StartFFmpegStreamAsync(string streamUrl)
        {
            try
            {
                // Создаем именованный pipe
                var fifoPath = "/data/data/com.tagos.oauthsample/files/input_fifo";
                Java.IO.File fifoFile = new Java.IO.File(fifoPath);

                if (!fifoFile.Exists())
                {
                    Java.Lang.Runtime.GetRuntime().Exec($"mkfifo {fifoPath}").WaitFor();
                }

                // Настраиваем FFmpeg для чтения из pipe
                string ffmpegArgs = $"-f rawvideo -pix_fmt yuv420p -s 1920x1080 -r 30 -i {fifoPath} " +
                                    $"-vcodec libx264 -preset veryfast -b:v 1000k -maxrate 1000k -bufsize 2000k -f flv {streamUrl}";

                Task.Run(() => FFmpeg.Execute(ffmpegArgs));

                // Открываем pipe и отправляем данные
                using (var outputStream = new FileOutputStream(fifoFile))
                {
                    while (_isCameraOpen)
                    {
                        // Получаем кадры из ImageReader и записываем их в pipe
                        var data = await CaptureFrameAsync();
                        if (data != null)
                        {
                            outputStream.Write(data);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Camera2Implementation", $"Ошибка при запуске стрима: {ex.Message}");
            }
        }

        // Пример асинхронного захвата кадра
        private Task<byte[]> CaptureFrameAsync()
        {
            // Реализуйте метод, возвращающий кадр из ImageReader
            return Task.FromResult<byte[]>(null);
        }


        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
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
            lock (_lock)
            {
                _isCameraOpen = false;
            }
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
                while (true)
                {
                    lock(_lock)
                    {
                        if(!_isCameraOpen)
                        {
                            break;
                        }
                    }
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
            if(!_isCameraOpen)
            {
                return;
            }

            //StartFFmpegStreamAsync(streamUrl);
            InitializeVlcStream(streamUrl);                     
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

     

public class CustomMediaInput : MediaInput
    {
        private readonly System.IO.Stream _dataStream;

        public CustomMediaInput(System.IO.Stream dataStream)
        {
            _dataStream = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
        }

       

       

        

        

            public override bool Open(out ulong size)
            {
                size = (ulong)_dataStream.Length;
                return true;
            }

            public override int Read(nint buffer, uint bufferSize)
            {
                var byteBuffer = new byte[bufferSize];
                int bytesRead = _dataStream.Read(byteBuffer, 0, (int)bufferSize);

                if (bytesRead > 0)
                {
                    System.Runtime.InteropServices.Marshal.Copy(byteBuffer, 0, buffer, bytesRead);
                }

                return bytesRead;
            }

            public override bool Seek(ulong offset)
            {
                if (!_dataStream.CanSeek)
                    return false;

                _dataStream.Seek((long)offset, SeekOrigin.Begin);
                return true;
            }

            public override void Close()
            {
                _dataStream.Dispose();
            }
        }


}
}
