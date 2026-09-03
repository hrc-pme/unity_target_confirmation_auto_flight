using UnityEngine;

namespace RosSharp.RosBridgeClient
{
    [RequireComponent(typeof(RosConnector))]
    public class ImageSubscriberPointCloud_v1 : UnitySubscriber<MessageTypes.Sensor.CompressedImage>
    {
        public MeshRenderer[] meshRenderers; // Changed from single MeshRenderer to array

        private Texture2D texture2D;
        private byte[] imageData;
        public byte[] ImageData
        {
            get { return imageData; }
        }
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

        protected override void Start()
        {
            base.Start();
            texture2D = new Texture2D(1, 1);

            // Initialize all MeshRenderers with a new material
            foreach (var meshRenderer in meshRenderers)
            {
                meshRenderer.material = new Material(Shader.Find("Standard"));
            }

            stamp = new MessageTypes.BuiltinInterfaces.Time();
            header = new MessageTypes.Std.Header();
        }

        private void Update()
        {
            if (isMessageReceived)
                ProcessMessage();
        }

        protected override void ReceiveMessage(MessageTypes.Sensor.CompressedImage compressedImage)
        {
            header = compressedImage.header;
            stamp = compressedImage.header.stamp;
            imageData = compressedImage.data;
            isMessageReceived = true;
        }

        private void ProcessMessage()
        {
            texture2D.LoadImage(imageData);
            texture2D.Apply();

            // Apply the texture to all MeshRenderers
            foreach (var meshRenderer in meshRenderers)
            {
                meshRenderer.material.SetTexture("_MainTex", texture2D);
            }

            isMessageReceived = false;
        }
    }
}
