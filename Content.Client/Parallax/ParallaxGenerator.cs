using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Nett;
using Robust.Client.Utility;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Noise;
using Robust.Shared.Random;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client.Parallax
{
    public sealed class ParallaxGenerator
    {
        private readonly List<Layer> _layers = new();

        public static Image<Rgba32> GenerateParallax(TomlTable config, Size size, ISawmill sawmill, List<Image<Rgba32>>? debugLayerDump, CancellationToken cancel = default)
        {
            sawmill.Debug("Generating parallax!");
            var generator = new ParallaxGenerator();
            generator.LoadConfig(config);

            sawmill.Debug("Timing start!");
            var sw = new Stopwatch();
            sw.Start();
            var image = new Image<Rgba32>(Configuration.Default, size.Width, size.Height, new Rgba32(0, 0, 0, 0));
            var count = 0;
            foreach (var layer in generator._layers)
            {
                cancel.ThrowIfCancellationRequested();
                layer.Apply(image);
                debugLayerDump?.Add(image.Clone());
                sawmill.Debug("Layer {0} done!", count++);
            }

            sw.Stop();
            sawmill.Debug("Total time: {0}", sw.Elapsed.TotalSeconds);

            return image;
        }

        private void LoadConfig(TomlTable config)
        {
            foreach (var layerArray in ((TomlTableArray)config.Get("layers")).Items)
            {
                switch (((TomlValue<string>)layerArray.Get("type")).Value)
                {
                    case "clear":
                        var layerClear = new LayerClear(layerArray);
                        _layers.Add(layerClear);
                        break;

                    case "toalpha":
                        var layerToAlpha = new LayerToAlpha(layerArray);
                        _layers.Add(layerToAlpha);
                        break;

                    case "noise":
                        var layerNoise = new LayerNoise(layerArray);
                        _layers.Add(layerNoise);
                        break;

                    case "points":
                        var layerPoint = new LayerPoints(layerArray);
                        _layers.Add(layerPoint);
                        break;

                    default:
                        throw new NotSupportedException();
                }
            }
        }

        private abstract class Layer
        {
            public abstract void Apply(Image<Rgba32> bitmap);
        }

        private abstract class LayerConversion : Layer
        {
            public abstract Color ConvertColor(Color input);

            public override void Apply(Image<Rgba32> bitmap)
            {
                var span = bitmap.GetPixelSpan();

                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var i = y * bitmap.Width + x;
                        span[i] = ConvertColor(span[i].ConvertImgSharp()).ConvertImgSharp();
                    }
                }
            }
        }

        private sealed class LayerClear : LayerConversion
        {
            private readonly Color _color = Color.Black;

            public LayerClear(TomlTable table)
            {
                if (table.TryGetValue("color", out var tomlObject))
                {
                    _color = Color.FromHex(((TomlValue<string>)tomlObject).Value);
                }
            }

            public override Color ConvertColor(Color input) => _color;
        }

        private sealed class LayerToAlpha : LayerConversion
        {
            public LayerToAlpha(TomlTable table)
            {
            }

            public override Color ConvertColor(Color input)
            {
                return new Color(input.R, input.G, input.B, MathF.Min(input.R + input.G + input.B, 1.0f));
            }
        }

        private sealed class LayerNoise : Layer
        {
            private readonly Color _innerColor = Color.White;
            private readonly Color _outerColor = Color.Black;
            private readonly FastNoiseLite.FractalType _noiseType = FastNoiseLite.FractalType.FBm;
            private readonly uint _seed = 1234;
            private readonly float _persistence = 0.5f;
            private readonly float _lacunarity = (float)(Math.PI / 3);
            private readonly float _frequency = 1;
            private readonly uint _octaves = 3;
            private readonly float _threshold;
            private readonly float _power = 1;
            private readonly Color.BlendFactor _srcFactor = Color.BlendFactor.One;
            private readonly Color.BlendFactor _dstFactor = Color.BlendFactor.One;

            public LayerNoise(TomlTable table)
            {
                if (table.TryGetValue("innercolor", out var tomlObject))
                {
                    _innerColor = Color.FromHex(((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("outercolor", out tomlObject))
                {
                    _outerColor = Color.FromHex(((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("seed", out tomlObject))
                {
                    _seed = (uint)((TomlValue<long>)tomlObject).Value;
                }

                if (table.TryGetValue("persistence", out tomlObject))
                {
                    _persistence = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("lacunarity", out tomlObject))
                {
                    _lacunarity = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("frequency", out tomlObject))
                {
                    _frequency = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("octaves", out tomlObject))
                {
                    _octaves = (uint)((TomlValue<long>)tomlObject).Value;
                }

                if (table.TryGetValue("threshold", out tomlObject))
                {
                    _threshold = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("sourcefactor", out tomlObject))
                {
                    _srcFactor = (Color.BlendFactor)Enum.Parse(typeof(Color.BlendFactor), ((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("destfactor", out tomlObject))
                {
                    _dstFactor = (Color.BlendFactor)Enum.Parse(typeof(Color.BlendFactor), ((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("power", out tomlObject))
                {
                    _power = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("noise_type", out tomlObject))
                {
                    switch (((TomlValue<string>)tomlObject).Value)
                    {
                        case "fbm":
                            _noiseType = FastNoiseLite.FractalType.FBm;
                            break;
                        case "ridged":
                            _noiseType = FastNoiseLite.FractalType.Ridged;
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                }
            }

            public override void Apply(Image<Rgba32> bitmap)
            {
                var noise = new FastNoiseLite((int)_seed);
                noise.SetFractalType(_noiseType);
                noise.SetFrequency(_frequency);
                noise.SetFractalLacunarity(_lacunarity);
                noise.SetFractalOctaves((int)_octaves);
                var threshVal = 1 / (1 - _threshold);
                var powFactor = 1 / _power;

                var span = bitmap.GetPixelSpan();

                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        // Do noise calculations.
                        var noiseVal = MathF.Min(1, MathF.Max(0, (noise.GetNoise(x, y) + 1) / 2));

                        // Threshold
                        noiseVal = MathF.Max(0, noiseVal - _threshold);
                        noiseVal *= threshVal;
                        noiseVal = MathF.Pow(noiseVal, powFactor);

                        // Get colors based on noise values.
                        var srcColor = Color.InterpolateBetween(_outerColor, _innerColor, noiseVal)
                            .WithAlpha(noiseVal);

                        // Apply blending factors & write back.
                        var i = y * bitmap.Width + x;
                        var dstColor = span[i].ConvertImgSharp();
                        span[i] = Color.Blend(dstColor, srcColor, _dstFactor, _srcFactor).ConvertImgSharp();
                    }
                }
            }
        }

        private sealed class LayerPoints : Layer
        {
            private readonly int _seed = 1234;
            private readonly int _pointCount = 100;

            private readonly Color _closeColor = Color.White;
            private readonly Color _farColor = Color.Black;

            private readonly Color.BlendFactor _srcFactor = Color.BlendFactor.One;
            private readonly Color.BlendFactor _dstFactor = Color.BlendFactor.One;

            // Noise mask stuff.
            private readonly bool _masked;
            private readonly FastNoiseLite.FractalType _maskNoiseType = FastNoiseLite.FractalType.FBm;
            private readonly uint _maskSeed = 1234;
            private readonly float _maskPersistence = 0.5f;
            private readonly float _maskLacunarity = (float)(Math.PI * 2 / 3);
            private readonly float _maskFrequency = 1;
            private readonly uint _maskOctaves = 3;
            private readonly float _maskThreshold;
            private readonly int _pointSize = 1;
            private readonly float _maskPower = 1;


            public LayerPoints(TomlTable table)
            {
                if (table.TryGetValue("seed", out var tomlObject))
                {
                    _seed = (int)((TomlValue<long>)tomlObject).Value;
                }

                if (table.TryGetValue("count", out tomlObject))
                {
                    _pointCount = (int)((TomlValue<long>)tomlObject).Value;
                }

                if (table.TryGetValue("sourcefactor", out tomlObject))
                {
                    _srcFactor = (Color.BlendFactor)Enum.Parse(typeof(Color.BlendFactor), ((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("destfactor", out tomlObject))
                {
                    _dstFactor = (Color.BlendFactor)Enum.Parse(typeof(Color.BlendFactor), ((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("farcolor", out tomlObject))
                {
                    _farColor = Color.FromHex(((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("closecolor", out tomlObject))
                {
                    _closeColor = Color.FromHex(((TomlValue<string>)tomlObject).Value);
                }

                if (table.TryGetValue("pointsize", out tomlObject))
                {
                    _pointSize = (int)((TomlValue<long>)tomlObject).Value;
                }

                // Noise mask stuff.
                if (table.TryGetValue("mask", out tomlObject))
                {
                    _masked = ((TomlValue<bool>)tomlObject).Value;
                }

                if (table.TryGetValue("maskseed", out tomlObject))
                {
                    _maskSeed = (uint)((TomlValue<long>)tomlObject).Value;
                }

                if (table.TryGetValue("maskpersistence", out tomlObject))
                {
                    _maskPersistence = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("masklacunarity", out tomlObject))
                {
                    _maskLacunarity = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("maskfrequency", out tomlObject))
                {
                    _maskFrequency = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("maskoctaves", out tomlObject))
                {
                    _maskOctaves = (uint)((TomlValue<long>)tomlObject).Value;
                }

                if (table.TryGetValue("maskthreshold", out tomlObject))
                {
                    _maskThreshold = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }

                if (table.TryGetValue("masknoise_type", out tomlObject))
                {
                    switch (((TomlValue<string>)tomlObject).Value)
                    {
                        case "fbm":
                            _maskNoiseType = FastNoiseLite.FractalType.FBm;
                            break;
                        case "ridged":
                            _maskNoiseType = FastNoiseLite.FractalType.Ridged;
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                }

                if (table.TryGetValue("maskpower", out tomlObject))
                {
                    _maskPower = float.Parse(((TomlValue<string>)tomlObject).Value, CultureInfo.InvariantCulture);
                }
            }

            public override void Apply(Image<Rgba32> bitmap)
            {
                // Temporary buffer so we don't mess up blending.
                using (var buffer = new Image<Rgba32>(Configuration.Default, bitmap.Width, bitmap.Height, new Rgba32(0, 0, 0, 0)))
                {
                    if (_masked)
                    {
                        GenPointsMasked(buffer);
                    }
                    else
                    {
                        GenPoints(buffer);
                    }

                    var srcSpan = buffer.GetPixelSpan();
                    var dstSpan = bitmap.GetPixelSpan();

                    var width = bitmap.Width;
                    var height = bitmap.Height;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var i = y * width + x;

                            var dstColor = dstSpan[i].ConvertImgSharp();
                            var srcColor = srcSpan[i].ConvertImgSharp();

                            dstSpan[i] = Color.Blend(dstColor, srcColor, _dstFactor, _srcFactor).ConvertImgSharp();
                        }
                    }
                }
            }

            private void GenPoints(Image<Rgba32> buffer)
            {
                var o = _pointSize - 1;
                var random = new Random(_seed);
                var span = buffer.GetPixelSpan();

                for (var i = 0; i < _pointCount; i++)
                {
                    var x = random.Next(0, buffer.Width);
                    var y = random.Next(0, buffer.Height);

                    var dist = random.NextFloat();

                    for (var oy = y - o; oy <= y + o; oy++)
                    {
                        for (var ox = x - o; ox <= x + o; ox++)
                        {
                            var ix = MathHelper.Mod(ox, buffer.Width);
                            var iy = MathHelper.Mod(oy, buffer.Height);

                            var color = Color.InterpolateBetween(_farColor, _closeColor, dist).ConvertImgSharp();
                            span[iy * buffer.Width + ix] = color;
                        }
                    }
                }
            }

            private void GenPointsMasked(Image<Rgba32> buffer)
            {
                var o = _pointSize - 1;
                var random = new Random(_seed);
                var noise = new FastNoiseLite((int)_maskSeed);
                noise.SetFractalType(_maskNoiseType);
                noise.SetFractalLacunarity(_maskLacunarity);
                noise.SetFractalOctaves((int)_maskOctaves);

                var threshVal = 1 / (1 - _maskThreshold);
                var powFactor = 1 / _maskPower;

                const int maxPointAttemptCount = 9999;
                var pointAttemptCount = 0;

                var span = buffer.GetPixelSpan();

                for (var i = 0; i < _pointCount; i++)
                {
                    var x = random.Next(0, buffer.Width);
                    var y = random.Next(0, buffer.Height);

                    // Grab noise at this point.
                    var noiseVal = MathF.Min(1, MathF.Max(0, (noise.GetNoise(x, y) + 1) / 2));
                    // Threshold
                    noiseVal = MathF.Max(0, noiseVal - _maskThreshold);
                    noiseVal *= threshVal;
                    noiseVal = MathF.Pow(noiseVal, powFactor);

                    var randomThresh = random.NextFloat();
                    if (randomThresh > noiseVal)
                    {
                        if (++pointAttemptCount <= maxPointAttemptCount)
                        {
                            i--;
                        }

                        continue;
                    }

                    var dist = random.NextFloat();

                    for (var oy = y - o; oy <= y + o; oy++)
                    {
                        for (var ox = x - o; ox <= x + o; ox++)
                        {
                            var ix = MathHelper.Mod(ox, buffer.Width);
                            var iy = MathHelper.Mod(oy, buffer.Height);

                            var color = Color.InterpolateBetween(_farColor, _closeColor, dist).ConvertImgSharp();
                            span[iy * buffer.Width + ix] = color;
                        }
                    }
                }
            }
        }
    }
}
