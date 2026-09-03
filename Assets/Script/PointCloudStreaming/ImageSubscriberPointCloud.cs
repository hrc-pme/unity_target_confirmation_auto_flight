using UnityEngine;
using UnityEngine.UI;

namespace RosSharp.RosBridgeClient
{
    [RequireComponent(typeof(RosConnector))]
    public class ImageSubscriberPointCloud : UnitySubscriber<MessageTypes.Sensor.Image>
    {
        public MeshRenderer[] meshRenderers;
        public RawImage[] rawImages;

        private Texture2D texture2D;
        private byte[] imageData;

        public byte[] ImageData
        {
            get { return imageData; }
        }

        /// <summary>
        /// 在主線程 ProcessMessage 中設定，保證其他主線程腳本可安全讀取
        /// </summary>
        public byte[] MainThreadImageData { get; private set; }

        private bool isMessageReceived;

        private MessageTypes.BuiltinInterfaces.Time stamp;
        private MessageTypes.Std.Header header;

        public MessageTypes.Std.Header HEADER
        {
            get { return header; }
        }

        public MessageTypes.BuiltinInterfaces.Time Stamp
        {
            get { return stamp; }
        }

        private uint imageWidth;
        private uint imageHeight;
        private string imageEncoding;
        private uint imageStep;

        protected override void Start()
        {
            base.Start();

            texture2D = new Texture2D(1, 1, TextureFormat.RGB24, false);

            Debug.LogWarning($"====== [Raw ImageSubscriber] START! Topic: {Topic} ======");
            Debug.LogWarning($"[Raw ImageSubscriber] MeshRenderers: {(meshRenderers != null ? meshRenderers.Length : 0)}");
            Debug.LogWarning($"[Raw ImageSubscriber] RawImages: {(rawImages != null ? rawImages.Length : 0)}");

            if (meshRenderers != null)
            {
                foreach (var meshRenderer in meshRenderers)
                {
                    if (meshRenderer != null)
                        meshRenderer.material = new Material(Shader.Find("Standard"));
                }
            }

            stamp = new MessageTypes.BuiltinInterfaces.Time();
            header = new MessageTypes.Std.Header();
        }

        private int noDataFrameCount = 0;

        private void Update()
        {
            if (isMessageReceived)
            {
                ProcessMessage();
                noDataFrameCount = 0;
            }
            else
            {
                noDataFrameCount++;

                if (noDataFrameCount == 300)
                {
                    Debug.LogError($"[{Topic}] ⚠️ 5秒內未收到任何 raw image！請檢查 ROS 端是否在發布。");
                }
                else if (noDataFrameCount % 600 == 0)
                {
                    Debug.LogWarning($"[{Topic}] 仍在等待 raw image... 已等待 {noDataFrameCount / 60} 秒");
                }
            }
        }

        protected override void ReceiveMessage(MessageTypes.Sensor.Image rawImage)
        {
            header = rawImage.header;
            stamp = rawImage.header.stamp;

            imageWidth = rawImage.width;
            imageHeight = rawImage.height;
            imageEncoding = rawImage.encoding;
            imageStep = rawImage.step;

            imageData = rawImage.data;
            isMessageReceived = true;

            Debug.Log($"[{Topic}] 收到 raw image: {imageWidth}x{imageHeight}, encoding={imageEncoding}, step={imageStep}, bytes={imageData.Length}");
        }

        private void ProcessMessage()
        {
            MainThreadImageData = imageData;

            try
            {
                int width = (int)imageWidth;
                int height = (int)imageHeight;

                if (width <= 0 || height <= 0 || imageData == null || imageData.Length == 0)
                {
                    Debug.LogWarning($"[{Topic}] raw image 資料不完整");
                    isMessageReceived = false;
                    return;
                }

                TextureFormat format = GetTextureFormat(imageEncoding);

                if (texture2D == null || texture2D.width != width || texture2D.height != height || texture2D.format != format)
                {
                    texture2D = new Texture2D(width, height, format, false);
                }

                byte[] convertedData = ConvertRosImageToUnityRaw(
                    imageData,
                    width,
                    height,
                    imageEncoding,
                    (int)imageStep
                );

                texture2D.LoadRawTextureData(convertedData);
                texture2D.Apply();

                // 旋轉 180 度，相機倒置修正
                Color32[] pixels = texture2D.GetPixels32();
                System.Array.Reverse(pixels);
                texture2D.SetPixels32(pixels);
                texture2D.Apply();

                Debug.Log($"[{Topic}] 處理 raw image 完成: {texture2D.width}x{texture2D.height}, encoding={imageEncoding}");

                int meshCount = 0;
                if (meshRenderers != null)
                {
                    foreach (var meshRenderer in meshRenderers)
                    {
                        if (meshRenderer != null)
                        {
                            meshRenderer.material.SetTexture("_MainTex", texture2D);
                            meshCount++;
                        }
                    }
                }

                int rawImageCount = 0;
                if (rawImages != null)
                {
                    foreach (var rawImage in rawImages)
                    {
                        if (rawImage != null)
                        {
                            rawImage.texture = texture2D;
                            rawImageCount++;
                        }
                    }
                }

                Debug.Log($"[{Topic}] 已更新: {meshCount} MeshRenderer(s), {rawImageCount} RawImage(s)");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[{Topic}] 處理 raw image 錯誤: {e.Message}");
            }

            isMessageReceived = false;
        }

        private TextureFormat GetTextureFormat(string encoding)
        {
            switch (encoding)
            {
                case "rgb8":
                case "bgr8":
                    return TextureFormat.RGB24;

                case "rgba8":
                case "bgra8":
                    return TextureFormat.RGBA32;

                case "mono8":
                    return TextureFormat.RGB24;

                default:
                    Debug.LogWarning($"[{Topic}] 未知 encoding={encoding}，先用 RGB24 嘗試");
                    return TextureFormat.RGB24;
            }
        }

        private byte[] ConvertRosImageToUnityRaw(
            byte[] source,
            int width,
            int height,
            string encoding,
            int step
        )
        {
            if (encoding == "rgb8")
            {
                return CopyRowsIfNeeded(source, width, height, 3, step);
            }

            if (encoding == "bgr8")
            {
                byte[] output = new byte[width * height * 3];

                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * step;
                    int dstRow = y * width * 3;

                    for (int x = 0; x < width; x++)
                    {
                        int srcIndex = srcRow + x * 3;
                        int dstIndex = dstRow + x * 3;

                        output[dstIndex + 0] = source[srcIndex + 2]; // R
                        output[dstIndex + 1] = source[srcIndex + 1]; // G
                        output[dstIndex + 2] = source[srcIndex + 0]; // B
                    }
                }

                return output;
            }

            if (encoding == "rgba8")
            {
                return CopyRowsIfNeeded(source, width, height, 4, step);
            }

            if (encoding == "bgra8")
            {
                byte[] output = new byte[width * height * 4];

                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * step;
                    int dstRow = y * width * 4;

                    for (int x = 0; x < width; x++)
                    {
                        int srcIndex = srcRow + x * 4;
                        int dstIndex = dstRow + x * 4;

                        output[dstIndex + 0] = source[srcIndex + 2]; // R
                        output[dstIndex + 1] = source[srcIndex + 1]; // G
                        output[dstIndex + 2] = source[srcIndex + 0]; // B
                        output[dstIndex + 3] = source[srcIndex + 3]; // A
                    }
                }

                return output;
            }

            if (encoding == "mono8")
            {
                byte[] output = new byte[width * height * 3];

                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * step;
                    int dstRow = y * width * 3;

                    for (int x = 0; x < width; x++)
                    {
                        byte gray = source[srcRow + x];
                        int dstIndex = dstRow + x * 3;

                        output[dstIndex + 0] = gray;
                        output[dstIndex + 1] = gray;
                        output[dstIndex + 2] = gray;
                    }
                }

                return output;
            }

            throw new System.NotSupportedException($"Unsupported image encoding: {encoding}");
        }

        private byte[] CopyRowsIfNeeded(byte[] source, int width, int height, int bytesPerPixel, int step)
        {
            int expectedStep = width * bytesPerPixel;
            int expectedSize = expectedStep * height;

            if (step == expectedStep && source.Length == expectedSize)
            {
                return source;
            }

            byte[] output = new byte[expectedSize];

            for (int y = 0; y < height; y++)
            {
                System.Buffer.BlockCopy(
                    source,
                    y * step,
                    output,
                    y * expectedStep,
                    expectedStep
                );
            }

            return output;
        }
    }
}