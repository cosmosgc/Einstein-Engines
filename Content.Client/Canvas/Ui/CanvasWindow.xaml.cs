using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.Ame.Components;
using Content.Shared.Canvas;
using Content.Shared.Crayon;
using Content.Shared.Decals;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Shared.Canvas.SharedCanvasComponent;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.Canvas.Ui
{
    [GenerateTypedNameReferences]
    public sealed partial class CanvasWindow : FancyWindow
    {
        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        private readonly SpriteSystem _spriteSystem;

        private NetEntity? _trackedEntity;
        private Texture? _blipTexture;

        private EntityUid? _owner = null;
        private CanvasUiKey _currentUiKey;

        private const int ButtonGridSize = 3;
        private int _height = 16;
        private int _width = 16;

        private string _paintingCode = string.Empty;

        private string _artist = string.Empty;
        private string _signature = string.Empty;

        private string? _autoSelected;
        private string? _selected;
        private Color _color = Color.Black;

        private const int ButtonSize = 30; // Size of each button (width and height)

        public event Action<Color>? OnColorSelected;
        public event Action<string>? OnSelected;
        public event Action<string>? OnFinalize;
        public event Action<string>? OnSignature;
        public event Action<int>? OnResizeHeight;
        public event Action<int>? OnResizeWidth;



        public CanvasWindow()
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            _spriteSystem = _entManager.System<SpriteSystem>();
            Search.OnTextChanged += SearchChanged;
            //ColorSelector.OnColorChanged += SelectColor;
            ImportButton.OnPressed += _ =>
            {
                string inputCode = Search.Text.Trim();

                // Ensure the input code is not empty
                if (!string.IsNullOrEmpty(inputCode))
                {
                    // If the input code is shorter than expected, fill the rest with 'W'
                    if (inputCode.Length < _height * _width)
                    {
                        inputCode = inputCode.PadRight(_height * _width, 'W');
                    }

                    // Update the painting code
                    _paintingCode = inputCode;

                    // Populate the grid with the new painting code
                    PopulatePaintingGrid();
                    OnSelected?.Invoke(_paintingCode);
                }
                else
                {
                    // Optionally, show an error message
                    Search.Text = "Invalid Code";
                }
            };

            ExportButton.OnPressed += _ =>
            {
                Search.Text = _paintingCode;
                FixPaintingCode();
                PopulatePaintingGrid();
            };

            HeightSize.OnReleased += _ =>
            {
                SetHeight((int) HeightSize.Value);
                HeightSizeLabel.Text = _height.ToString();
                OnResizeHeight?.Invoke(_height);
                PopulatePaintingGrid();
            };

            HeightSize.OnValueChanged += _ =>
            {
                HeightSizeLabel.Text = HeightSize.Value.ToString();
            };

            WidthSize.OnReleased += _ =>
            {
                SetWidth((int) WidthSize.Value);
                WidthSizeLabel.Text = _width.ToString();
                OnResizeWidth?.Invoke(_width);
                PopulatePaintingGrid();
            };
            WidthSize.OnValueChanged += _ =>
            {
                WidthSizeLabel.Text = WidthSize.Value.ToString();
            };

            FinalizeButton.OnPressed += _ =>
            {
                _artist = "Anonymous";
                if (!string.IsNullOrEmpty(_signature))
                    _artist = _signature;
                if (_entManager.TryGetComponent(_owner, out MetaDataComponent? metaData))
                {
                    _artist = metaData.EntityName;
                }
                OnFinalize?.Invoke(_artist);
            };

            ArtistSignature.OnTextEntered += _ =>
            {
                _signature = ArtistSignature.Text;
                OnSignature?.Invoke(_signature);
            };
            //FixPaintingCode();
            //PopulatePaintingGrid();
        }

        private void SelectColor(Color color)
        {
            _color = color;

            OnColorSelected?.Invoke(color);
            //RefreshList();
        }

        private void SearchChanged(LineEdit.LineEditEventArgs obj)
        {
            //_autoSelected = ""; // Placeholder to kick off the auto-select in refreshlist()
            //RefreshList();
        }

        public void UpdateState(BoundUserInterfaceState state)
        {
            var castState = (CanvasBoundUserInterfaceState) state;
            _selected = castState.Selected;
            ColorSelector.Visible = castState.SelectableColor;
            _color = castState.Color;
            _paintingCode = castState.PaintingCode;
            _height = castState.Height;
            _width = castState.Width;
            _artist = castState.Artist;
            //Logger.ErrorS("canvas", $"received update {_paintingCode}.");

            PopulatePaintingGrid();
        }
        public void AdvanceState(string drawn)
        {
            //var filter = Search.Text;
            //if (!filter.Contains(',') || !filter.Contains(drawn))
            //    return;

            //var first = filter[..filter.IndexOf(',')].Trim();

            //if (first.Equals(drawn, StringComparison.InvariantCultureIgnoreCase))
            //{
            //    Search.Text = filter[(filter.IndexOf(',') + 1)..].Trim();
            //    _autoSelected = first;
            //}

            //RefreshList();
        }

        private void HandleColorSelected(Color color)
        {
            _color = color; // Update the current selected color
            ColorPreview.ModulateSelfOverride = color;
            OnColorSelected?.Invoke(color); // Trigger the event for external handlers
        }

        public void PopulateColorSelector(List<Color> colors)
        {
            // Clear existing children in ColorSelector
            ColorSelector.RemoveAllChildren();

            // Create a new BoxContainer for each set of 10 colors
            BoxContainer? colorGroup = null;
            int colorCount = 0;

            foreach (var color in colors)
            {
                // Create a new BoxContainer every 10 colors
                if (colorCount % 16 == 0)
                {
                    // If colorGroup already exists, add it to the ColorSelector before starting a new one
                    if (colorGroup != null)
                    {
                        ColorSelector.AddChild(colorGroup);
                    }

                    // Create a new BoxContainer for the next 10 colors
                    colorGroup = new BoxContainer
                    {
                        Orientation = LayoutOrientation.Vertical,
                    };
                }

                // Create a button for each color
                var colorButton = new Button
                {
                    MinSize = new Vector2(ButtonSize, ButtonSize),
                    MaxSize = new Vector2(ButtonSize, ButtonSize),
                    ModulateSelfOverride = color, // Set the button background to the color
                };

                // Attach an event to handle color selection
                colorButton.OnPressed += _ => HandleColorSelected(color);

                // Add the color button to the current BoxContainer (group)
                if (colorGroup != null)
                    colorGroup.AddChild(colorButton);

                colorCount++;
            }

            // Add the last group if it contains any colors
            if (colorGroup != null && colorGroup.ChildCount > 0)
            {
                ColorSelector.AddChild(colorGroup);
            }

            // Add a button specifically for transparency
            var transparencyButton = new Button
            {
                Text = "Eraser",
                MinHeight = 30,
                VerticalAlignment = Control.VAlignment.Top
            };

            // Attach an event to handle transparency selection
            transparencyButton.OnPressed += _ => HandleColorSelected(Color.Transparent);

            // Add the transparency button to the ColorSelector
            ResolutionContainer.AddChild(transparencyButton);
        }


        public void SetPaintingCode(string code)
        {
            _paintingCode = code;
        }
        public void SetArtist(string artist)
        {
            _artist = artist;
        }
        public void SetSignature(string signature)
        {
            _signature = signature;
            ArtistSignature.Text = _signature;
        }
        public void SetHeight(int height)
        {
            _height = height;
            HeightSize.Value = height;
        }
        public void SetWidth(int width)
        {
            _width = width;
            WidthSize.Value = width;
        }
        public void PopulatePaintingGrid()
        {
            // Clear any existing children in the Grids container
            Grids.RemoveAllChildren();

            if (!string.IsNullOrEmpty(_artist))
            {
                ResolutionContainer.Visible = false;
                HeaderColorPreview.Visible = false;
                HeaderTools.Visible = false;
            }

            int index = 0; // Index to track the position in the painting code
            bool isDrawing = false; // Tracks if the mouse button is held down for drawing

            // Split the painting code into individual color segments
            string[] colorSegments = _paintingCode.Split(';', StringSplitOptions.RemoveEmptyEntries);

            // Iterate over rows
            for (int row = 0; row < _height; row++)
            {
                int currentRow = row;

                // Create a new horizontal BoxContainer for each row
                var rowContainer = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    VerticalExpand = false,
                    Align = AlignMode.Center
                };

                // Iterate over columns for the current row
                for (int col = 0; col < _width; col++)
                {
                    int currentCol = col;

                    // Get the initial color for this cell, defaulting to white if index out of bounds
                    Color initialColor = index < colorSegments.Length
                        ? GetColorFromCode(colorSegments[index])
                        : Color.White;

                    index++; // Increment the index

                    // Create a new button
                    var button = new Button
                    {
                        MinSize = new Vector2(ButtonSize, ButtonSize),
                        MaxSize = new Vector2(ButtonSize, ButtonSize),
                        StyleClasses = { "OpenBoth" },
                        ModulateSelfOverride = initialColor
                    };

                    // Handle mouse-down events for starting the drawing process
                    button.OnButtonDown += _ =>
                    {
                        isDrawing = true;
                        button.ModulateSelfOverride = _color;
                        UpdatePaintingCode(currentRow, currentCol, _color);
                    };

                    // Handle mouse-enter events for drawing while holding the mouse button
                    button.OnMouseEntered += _ =>
                    {
                        if (isDrawing)
                        {
                            button.ModulateSelfOverride = _color;
                            UpdatePaintingCode(currentRow, currentCol, _color);
                        }
                    };

                    // Handle mouse-up events to stop drawing
                    button.OnButtonUp += _ => isDrawing = false;

                    rowContainer.AddChild(button);
                }

                Grids.AddChild(rowContainer);
            }

            if (!string.IsNullOrEmpty(_artist))
            {
                var artistButton = new Button
                {
                    Text = _artist,
                    ModulateSelfOverride = Color.Black
                };
                Grids.AddChild(artistButton);
            }
        }



        /// <summary>
        /// Updates the painting code when a button's color changes.
        /// </summary>
        private void UpdatePaintingCode(int row, int col, Color color)
        {
            if (!string.IsNullOrEmpty(_artist))
                return;

            FixPaintingCode();

            int index = row * _width + col;

            // Split the painting code into individual segments
            string[] colorSegments = _paintingCode.Split(';', StringSplitOptions.RemoveEmptyEntries);

            if (index < 0 || index >= colorSegments.Length)
            {
                Logger.ErrorS("canvas", $"Index {index} is out of bounds for painting code.");
                return;
            }

            // Update the color at the specified index
            colorSegments[index] = $"{color.R:F2}|{color.G:F2}|{color.B:F2}|{color.A:F2}";

            // Reconstruct the painting code as a string
            _paintingCode = string.Join(";", colorSegments);
            OnSelected?.Invoke(_paintingCode);
        }

        /// <summary>
        /// Converts a color code character to a Color object.
        /// </summary>
        private Color GetColorFromCode(string colorCode)
        {
            colorCode = new string(colorCode
                .Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))
                .ToArray());

            colorCode = colorCode.Replace('.', ',');
            string[] components = colorCode.Split('|');
            if (components.Length == 4 &&
                float.TryParse(components[0], out float r) &&
                float.TryParse(components[1], out float g) &&
                float.TryParse(components[2], out float b) &&
                float.TryParse(components[3], out float a))
            {
                return new Color(r, g, b, a);
            }

            return Color.White; // Default to white if parsing fails
        }


        /// <summary>
        /// Converts a Color object to a color code character.
        /// </summary>
        private char GetCodeFromColor(Color color)
        {
            if (color == Color.Transparent) return 'Z';
            if (color == Color.Red) return 'R';
            if (color == Color.Green) return 'G';
            if (color == Color.Blue) return 'B';
            if (color == Color.Yellow) return 'Y';
            if (color == Color.Cyan) return 'C';
            if (color == Color.Magenta) return 'M';
            if (color == new Color(1.0f, 0.65f, 0.0f)) return 'O'; // Orange
            if (color == new Color(0.75f, 0.0f, 0.75f)) return 'P'; // Purple
            if (color == new Color(0.33f, 0.55f, 0.2f)) return 'T'; // Teal
            if (color == new Color(0.6f, 0.3f, 0.1f)) return 'N'; // Brown
            if (color == new Color(0.9f, 0.8f, 0.7f)) return 'E'; // Beige
            if (color == Color.LightGray) return 'L';
            if (color == Color.DarkGray) return 'D';
            if (color == new Color(0.5f, 0.5f, 1.0f)) return 'F'; // Pastel Blue
            if (color == new Color(1.0f, 0.5f, 0.5f)) return 'I'; // Pastel Pink
            if (color == new Color(0.0f, 0.5f, 0.5f)) return 'Q'; // Dark Cyan
            if (color == new Color(0.4f, 0.2f, 0.6f)) return 'H'; // Deep Purple
            if (color == Color.Black) return 'K';
            return 'W'; // Default to white
        }


        public void InitializeFromYaml(string paintingCode)
        {
            if (!string.IsNullOrEmpty(paintingCode))
            {
                _paintingCode = paintingCode;
            }

            //PopulatePaintingGrid();
        }
        private void FixPaintingCode()
        {
            int requiredSegments = _width * _height;

            // Split existing segments and count them
            string[] colorSegments = _paintingCode.Split(';', StringSplitOptions.RemoveEmptyEntries);

            if (colorSegments.Length < requiredSegments)
            {
                // Add default colors (white) for missing segments
                Color color = Color.White;
                var defaultColor = $"{color.R:F2}|{color.G:F2}|{color.B:F2}|{color.A:F2}";
                var paddedSegments = colorSegments.ToList();

                while (paddedSegments.Count < requiredSegments)
                {
                    paddedSegments.Add(defaultColor);
                }

                _paintingCode = string.Join(";", paddedSegments);
            }
        }


    }
}