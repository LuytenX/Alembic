using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ACViewer.Config;

namespace ACViewer.Model
{
    public class SaveMeter
    {
        public static GraphicsDevice GraphicsDevice => GameView.Instance.GraphicsDevice;
        public static SpriteBatch spriteBatch => GameView.Instance.SpriteBatch;
        public static SpriteFont Font => GameView.Instance.Font;

        public bool IsSaving { get; set; }
        public float Percent { get; set; }

        private static Texture2D TextureBlack;
        private static Texture2D TextureProgress;

        static SaveMeter()
        {
            var colors = new Color[1];
            colors[0] = Color.Black;
            TextureBlack = new Texture2D(GraphicsDevice, 1, 1);
            TextureBlack.SetData(colors);

            colors = new Color[1];
            // Use the configured progress bar color, defaulting to LimeGreen if not set
            colors[0] = Config.ConfigManager.Config.BackgroundColors.ProgressBar;
            TextureProgress = new Texture2D(GraphicsDevice, 1, 1);
            TextureProgress.SetData(colors);
        }

        public void Start()
        {
            Percent = 0.0f;
            IsSaving = true;
        }

        public void Stop()
        {
            IsSaving = false;
        }

        public void Draw()
        {
            if (!IsSaving || spriteBatch == null) return;

            var viewport = GraphicsDevice.Viewport;
            int width = 200;
            int height = 15;
            int margin = 20;
            
            // Lower-left position
            int x = margin;
            int y = viewport.Height - height - margin;

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            
            // Draw background
            var bgRect = new Rectangle(x, y, width, height);
            spriteBatch.Draw(TextureBlack, bgRect, Color.White * 0.7f);
            
            // Draw progress
            var progressRect = new Rectangle(x, y, (int)(Percent * width), height);
            spriteBatch.Draw(TextureProgress, progressRect, Color.White);

            // Draw "Saving..." text if font is available
            if (Font != null)
            {
                spriteBatch.DrawString(Font, "Saving...", new Vector2(x, y - 20), Color.White);
            }

            spriteBatch.End();
        }
    }
}
