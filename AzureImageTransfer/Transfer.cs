using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Net;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace AzureImageTransfer
{
    public class ResizingSettings
    {
        public int Width { get; set; }
        public int? Height { get; set; }
        public bool Crop { get; set; }
        public System.Drawing.Drawing2D.InterpolationMode InterpolationMode { get; set; }
        public System.Drawing.Drawing2D.CompositingQuality CompositingQuality { get; set; }
        public System.Drawing.Drawing2D.SmoothingMode SmoothingMode { get; set; }

        public ResizingSettings()
        {
            InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Default;
            CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.Default;
            SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        }

        public ResizingSettings(int width, int? height, bool crop) : this()
        {
            Width = width;
            Height = height;
            Crop = crop;
        }
    }

    public class ImageTransfer
    {
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string ContainerName { get; set; }
        public bool UseDevelopmentStorage { get; set; }

        private CloudBlobContainer container;
        
        public ImageTransfer()
        {
            UseDevelopmentStorage = false;
        }

        public ImageTransfer(string accountName, string accountKey, string containerName) : this()
        {
            AccountName = accountName;
            AccountKey = accountKey;
            ContainerName = containerName;
        }

        /// <summary>
        /// To connect to local development storage
        /// </summary>
        /// <param name="containerName"></param>
        public ImageTransfer(string containerName) 
        {
            ContainerName = containerName;
            UseDevelopmentStorage = true;
        }
        
        public void Connect()
        {
            if (!UseDevelopmentStorage)
            {
                if (string.IsNullOrEmpty(AccountName))
                {
                    throw new ArgumentNullException("AccountName");
                }

                if (string.IsNullOrEmpty(AccountKey))
                {
                    throw new ArgumentNullException("AccountKey");
                }
            }

            if (string.IsNullOrEmpty(ContainerName))
            {
                throw new ArgumentNullException("ContainerName");
            }

            string connectionString;

            if (UseDevelopmentStorage) 
            {
                connectionString = "UseDevelopmentStorage=true";
            }
            else
            {
                connectionString = String.Format("DefaultEndpointsProtocol=http;AccountName={0};AccountKey={1}", AccountName, AccountKey);
            }

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            container = blobClient.GetContainerReference(ContainerName);
        }
        
        public string Transfer(string imageUrl, string blobName) 
        {
            string azureUri = null;
            if (string.IsNullOrEmpty(imageUrl))
            {
                throw new ArgumentNullException("imageUrl");
            }

            if (string.IsNullOrEmpty(blobName))
            {
                throw new ArgumentNullException("blobName");
            }

            //Retrieves stream from image url
            Stream imageStream = DownloadRemoteImageFile(imageUrl);

            azureUri = UploadToAzureStorage(blobName, imageStream);

            return azureUri;
        }

        public List<string> ResizeAndTransfer(string imageUrl, string blobName, bool alsoUploadOriginalImage, List<ResizingSettings> resizingSettings)
        {

            if (string.IsNullOrEmpty(imageUrl))
            {
                throw new ArgumentNullException("imageUrl");
            }

            if (string.IsNullOrEmpty(blobName))
            {
                throw new ArgumentNullException("blobName");
            }

            if (resizingSettings == null)
            {
                throw new ArgumentNullException("resizingSettings");
            }

            string extension = Path.GetExtension(blobName);
            if (string.IsNullOrEmpty(extension))
            {
                throw new ArgumentException(string.Format("Unable to determine file extension for fileName: {0}", blobName));
            }

            List<string> resizedAzureUri = new List<string>();
            string blobNameWithoutExtension = Path.GetFileNameWithoutExtension(blobName);

            //Retrieves stream from image url
            using (Stream imageStream = DownloadRemoteImageFile(imageUrl))
            using (MemoryStream memoryStream = new MemoryStream())
            {
                imageStream.CopyTo(memoryStream);
                //start creating and uploading resized versions
                Image imageBitmap = Image.FromStream(memoryStream);
                //Upload image to Azure storage
                memoryStream.Seek(0, SeekOrigin.Begin);
                if (alsoUploadOriginalImage)
                {
                    resizedAzureUri.Add(UploadToAzureStorage(blobName, memoryStream));
                }

                foreach (var item in resizingSettings)
                {
                    var resizedImage = this.Resize(imageBitmap, item.Width, item.Height, item.Crop);
                    using (var stream = new MemoryStream())
                    {
                        resizedImage.Save(stream, GetImageFormat(extension));
                        stream.Position = 0;
                        string resizedBlobName = string.Format("{0}_{1}x{2}{3}", blobNameWithoutExtension, item.Width, item.Height.HasValue ? item.Height.Value.ToString() : "_", extension);
                        resizedAzureUri.Add(UploadToAzureStorage(resizedBlobName, stream));
                    }
                }
            }


            return resizedAzureUri;
        }

        public string UploadToAzureStorage(string blobName, Stream imageStream)
        {
            if (imageStream == null)
            {
                throw new ArgumentNullException("imageStream");
            }

            if (container == null)
            {
                throw new Exception("You must call the Connect() method first");
            }
            
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);
            blockBlob.Properties.ContentType = "image/jpg";

            using (imageStream)
            {
                blockBlob.UploadFromStream(imageStream);
            }

            return container.Uri.AbsoluteUri + "/" + blobName;
        }

        public Stream DownloadRemoteImageFile(string uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception)
            {
                return null;
            }

            // Check that the remote file was found. The ContentType
            // check is performed since a request for a non-existent
            // image file might be redirected to a 404-page, which would
            // yield the StatusCode "OK", even though the image was not
            // found.
            if ((response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Moved ||
                response.StatusCode == HttpStatusCode.Redirect) &&
                response.ContentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {

                // if the remote file was found, download it
                return response.GetResponseStream();
            }
            else{
                return null;
            }
        }

        public Image Resize(Image image, int width, int? height, bool crop)
        {
            int sourceWidth = image.Width;
            int sourceHeight = image.Height;
            int sourceX = 0;
            int sourceY = 0;
            double destX = 0;
            double destY = 0;

            double nScale = 0;
            double nScaleW = 0;
            double nScaleH = 0;

            nScaleW = ((double)width / (double)sourceWidth);

            if (!height.HasValue)
            {
                height = (int)(sourceHeight * nScaleW);
            }

            nScaleH = ((double)height / (double)sourceHeight);
            if (!crop)
            {
                nScale = Math.Min(nScaleH, nScaleW);
            }
            else
            {
                nScale = Math.Max(nScaleH, nScaleW);
                destY = (height.Value - sourceHeight * nScale) / 2;
                destX = (width - sourceWidth * nScale) / 2;
            }

            //if (nScale > 1)
            //    nScale = 1;

            int destWidth = (int)Math.Round(sourceWidth * nScale);
            int destHeight = (int)Math.Round(sourceHeight * nScale);

            System.Drawing.Bitmap bmPhoto = null;
            try
            {
                bmPhoto = new System.Drawing.Bitmap(destWidth + (int)Math.Round(2 * destX), destHeight + (int)Math.Round(2 * destY));
            }
            catch (Exception ex)
            {
                throw new ApplicationException(string.Format("destWidth:{0}, destX:{1}, destHeight:{2}, desxtY:{3}, Width:{4}, Height:{5}",
                    destWidth, destX, destHeight, destY, width, height), ex);
            }
            using (System.Drawing.Graphics grPhoto = System.Drawing.Graphics.FromImage(bmPhoto))
            {
                grPhoto.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
                grPhoto.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed; 
                grPhoto.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                Rectangle to = new System.Drawing.Rectangle((int)Math.Round(destX), (int)Math.Round(destY), destWidth, destHeight);
                Rectangle from = new System.Drawing.Rectangle(sourceX, sourceY, sourceWidth, sourceHeight);
                grPhoto.DrawImage(image, to, from, System.Drawing.GraphicsUnit.Pixel);

                return bmPhoto;
            }
        }
        
        private ImageFormat GetImageFormat(string extension)
        {
            switch (extension.ToLower())
            {
                case @".bmp":
                    return ImageFormat.Bmp;
                case @".gif":
                    return ImageFormat.Gif;
                case @".ico":
                    return ImageFormat.Icon;
                case @".jpg":
                case @".jpeg":
                    return ImageFormat.Jpeg;
                case @".png":
                    return ImageFormat.Png;
                case @".tif":
                case @".tiff":
                    return ImageFormat.Tiff;
                case @".wmf":
                    return ImageFormat.Wmf;
                default:
                    throw new NotImplementedException();
            }
        }



    }
}
