using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class AutoChartistTradingPanel : Robot
    {
        [Parameter("Vertical Position", Group = "Panel alignment", DefaultValue = VerticalAlignment.Bottom)]
        public VerticalAlignment PanelVerticalAlignment { get; set; }

        [Parameter("Horizontal Position", Group = "Panel alignment", DefaultValue = HorizontalAlignment.Left)]
        public HorizontalAlignment PanelHorizontalAlignment { get; set; }

        [Parameter("Volume", Group = "Default Trade parameters", DefaultValue = 2000)]
        public int Volume { get; set; }

        [Parameter("Trail Stop ATR", Group = "Default Trade parameters", DefaultValue = 1.5)]
        public double TrailStopAtr { get; set; }

        [Parameter("Stop Loss ATR", Group = "Default Trade parameters", DefaultValue = 1.5)]
        public double StopLossAtr { get; set; }

        [Parameter("Take Profit ATR", Group = "Default Trade parameters", DefaultValue = 1.0)]
        public double TakeProfitAtr { get; set; }

        [Parameter("Expire After (Minutes)", Group = "Default Trade parameters", DefaultValue = 1)]
        public int ExpireAfterMinutes { get; set; }

        [Parameter("Period", Group = "ATR parameters", DefaultValue = 14)]
        public int AtrPeriod { get; set; }

        [Parameter("Moving Average", Group = "ATR parameters", DefaultValue = MovingAverageType.Exponential)]
        public MovingAverageType AtrMaType { get; set; }

        private const string label = "AutoChartistTradingPanel";
        private AverageTrueRange averageTrueRange;
        TradingPanel tradingPanel;

        protected override void OnStart()
        {
            Positions.Closed += OnPositionsClosed;

            averageTrueRange = Indicators.AverageTrueRange(AtrPeriod, AtrMaType);

            tradingPanel = new TradingPanel(this, Volume, TrailStopAtr, StopLossAtr, TakeProfitAtr, ExpireAfterMinutes, averageTrueRange);

            var border = new Border 
            {
                VerticalAlignment = PanelVerticalAlignment,
                HorizontalAlignment = PanelHorizontalAlignment,
                Style = Styles.CreatePanelBackgroundStyle(),
                Margin = "20 40 20 20",
                Width = 225,
                Child = tradingPanel
            };

            Chart.AddControl(border);
        }

        protected override void OnBar()
        {
            Position[] positions = Positions.FindAll(label, Symbol.Name);
            if (positions.Length == 1)
            {
                foreach (Position remainingPosition in positions)
                {
                    double atrSnapshotValue = tradingPanel.GetOutputInfoValue("AtrValueSnapshotKey");
                    //double trailAtrSnapshot = tradingPanel.GetOutputInfoValue("TrailAtrSnapshotKey");
                    if (remainingPosition.TradeType == TradeType.Buy)
                    {
                        double atrTrailPositionPrice = Symbol.Bid - atrSnapshotValue * TrailStopAtr;
                        if (atrTrailPositionPrice > remainingPosition.StopLoss)
                        {
                            remainingPosition.ModifyStopLossPrice(atrTrailPositionPrice);
                            Print("Successfully Trailed.");
                        }
                    }
                    else
                    {
                        double atrTrailPositionPrice = Symbol.Ask + atrSnapshotValue * TrailStopAtr;
                        if (atrTrailPositionPrice < remainingPosition.StopLoss)
                        {
                            remainingPosition.ModifyStopLossPrice(atrTrailPositionPrice);
                            Print("Successfully Trailed.");
                        }
                    }
                }
            }
        }

        protected override void OnStop()
        {
            // Put your deinitialization logic here
        }

        private void OnPositionsClosed(PositionClosedEventArgs args)
        {
            Position position = args.Position;

            if (position.Label != label || position.SymbolName != SymbolName)
                return;

            Position[] positions = Positions.FindAll(label, SymbolName);

            if (positions.Length == 1)
            {
                foreach (Position remainingPosition in positions)
                {
                    // move the remaining position stoploss to breakeven or ATR trail position, whichever closer to current price.
                    double atrSnapshotValue = tradingPanel.GetOutputInfoValue("AtrValueSnapshotKey");
                    //double trailAtrSnapshot = tradingPanel.GetOutputInfoValue("TrailAtrSnapshotKey");
                    if (remainingPosition.TradeType == TradeType.Buy)
                    {
                        double atrTrailPositionPrice = Symbol.Bid - atrSnapshotValue * TrailStopAtr;
                        if (atrTrailPositionPrice > remainingPosition.EntryPrice)
                            remainingPosition.ModifyStopLossPrice(atrTrailPositionPrice);
                        else
                            remainingPosition.ModifyStopLossPrice(remainingPosition.EntryPrice + Symbol.PipSize);
                    }
                    else
                    {
                        double atrTrailPositionPrice = Symbol.Ask + atrSnapshotValue * TrailStopAtr;
                        if (atrTrailPositionPrice < remainingPosition.EntryPrice)
                            remainingPosition.ModifyStopLossPrice(atrTrailPositionPrice);
                        else
                            remainingPosition.ModifyStopLossPrice(remainingPosition.EntryPrice - Symbol.PipSize);
                    }
                }
                Print("Successfully Moved StopLoss.");
            }
        }

        public class TradingPanel : CustomControl
        {
            private const string TakeProfitPriceInputKey = "TakeProfitPriceKey";
            private const string StopLossPriceInputKey = "StopLossPriceKey";
            private const string EntryPriceInputKey = "EntryPriceKey";
            private const string VolumeInputKey = "VolumeKey";
            //private const string TrailAtrInputKey = "TrailAtrKey";
            private const string ExpireAfterMinutesInputKey = "ExpireAfterMinutesKey";
            private readonly IDictionary<string, TextBox> _inputMap = new Dictionary<string, TextBox>();
            private readonly Robot _robot;
            private const string AtrValueSnapshotOutputKey = "AtrValueSnapshotKey";
            private const string TrailStopAtrSnapshotOutputKey = "TrailStopAtrSnapshotKey";
            private AverageTrueRange _averageTrueRange;
            private readonly IDictionary<string, TextBox> _outputMap = new Dictionary<string, TextBox>();
            private double _stopLossAtr;
            private double _takeProfitAtr;

            public TradingPanel(Robot robot, double volume, double trailStopAtr, double stopLossAtr, double takeProfitAtr, int expireAfterMinutes, AverageTrueRange averageTrueRange)
            {
                _robot = robot;
                _averageTrueRange = averageTrueRange;
                _stopLossAtr = stopLossAtr;
                _takeProfitAtr = takeProfitAtr;
                AddChild(CreateTradingPanel(volume, trailStopAtr, expireAfterMinutes));
            }

            private ControlBase CreateTradingPanel(double volume, double trailStopAtr, int expireAfterMinutes)
            {
                var mainPanel = new StackPanel();

                var header = CreateHeader();
                mainPanel.AddChild(header);

                var contentPanel = CreateContentPanel(volume, trailStopAtr, expireAfterMinutes);
                mainPanel.AddChild(contentPanel);

                var infoHeader = CreateInfoHeader();
                mainPanel.AddChild(infoHeader);

                var infoPanel = CreateInfoPanel();
                mainPanel.AddChild(infoPanel);

                return mainPanel;
            }

            private ControlBase CreateHeader()
            {
                var headerBorder = new Border 
                {
                    BorderThickness = "0 0 0 1",
                    Style = Styles.CreateCommonBorderStyle()
                };

                var header = new TextBlock 
                {
                    Text = "Trading Panel",
                    Margin = "10 7",
                    Style = Styles.CreateHeaderStyle()
                };

                headerBorder.Child = header;
                return headerBorder;
            }

            private StackPanel CreateContentPanel(double volume, double trailStopAtr, int expireAfterMinutes)
            {
                var contentPanel = new StackPanel 
                {
                    Margin = 10
                };
                var grid = new Grid(4, 3);
                grid.Columns[1].SetWidthInPixels(5);

                var sellButton = CreateTradeButton("SELL", Styles.CreateSellButtonStyle(), TradeType.Sell);
                grid.AddChild(sellButton, 0, 0);

                var buyButton = CreateTradeButton("BUY", Styles.CreateBuyButtonStyle(), TradeType.Buy);
                grid.AddChild(buyButton, 0, 2);

                var lotsInput = CreateInputWithLabel("Volume", volume.ToString(), VolumeInputKey);
                grid.AddChild(lotsInput, 1, 0, 1, 3);

                //var trailAtrInput = CreateInputWithLabel("Trail ATR", trailStopAtr.ToString(), TrailAtrInputKey);
                //grid.AddChild(trailAtrInput, 1, 2);

                var stopLossInput = CreateInputWithLabel("Stop Loss", "", StopLossPriceInputKey);
                grid.AddChild(stopLossInput, 2, 0);

                var takeProfitInput = CreateInputWithLabel("Take Profit", "", TakeProfitPriceInputKey);
                grid.AddChild(takeProfitInput, 2, 2);

                var entryInput = CreateInputWithLabel("Entry", "", EntryPriceInputKey);
                grid.AddChild(entryInput, 3, 0);

                var expireAfterMinutesInput = CreateInputWithLabel("Expire (Minutes)", expireAfterMinutes.ToString(), ExpireAfterMinutesInputKey);
                grid.AddChild(expireAfterMinutesInput, 3, 2);

                contentPanel.AddChild(grid);

                return contentPanel;
            }

            private Button CreateTradeButton(string text, Style style, TradeType tradeType)
            {
                var tradeButton = new Button 
                {
                    Text = text,
                    Style = style,
                    Height = 25
                };

                tradeButton.Click += args => ExecuteOrderAsync(tradeType);

                return tradeButton;
            }

            private Panel CreateInputWithLabel(string label, string defaultValue, string inputKey)
            {
                var stackPanel = new StackPanel 
                {
                    Orientation = Orientation.Vertical,
                    Margin = "0 10 0 0"
                };

                var textBlock = new TextBlock 
                {
                    Text = label
                };

                var input = new TextBox 
                {
                    Margin = "0 5 0 0",
                    Text = defaultValue,
                    Style = Styles.CreateInputStyle()
                };

                _inputMap.Add(inputKey, input);

                stackPanel.AddChild(textBlock);
                stackPanel.AddChild(input);

                return stackPanel;
            }

            private Panel CreateOutputWithLabel(string label, string defaultValue, string inputKey)
            {
                var stackPanel = new StackPanel 
                {
                    Orientation = Orientation.Vertical,
                    Margin = "0 10 0 0"
                };

                var textBlock = new TextBlock 
                {
                    Text = label
                };

                var input = new TextBox 
                {
                    Margin = "0 5 0 0",
                    Text = defaultValue,
                    Style = Styles.CreateInputStyle()
                };

                _outputMap.Add(inputKey, input);

                stackPanel.AddChild(textBlock);
                stackPanel.AddChild(input);

                return stackPanel;
            }

            /*private StackPanel CreateInfoPanel()
            {
                var infoPanel = new StackPanel 
                {
                    Orientation = Orientation.Vertical,
                    Margin = 10
                };

                var infoHeader = new TextBlock 
                {
                    Text = "Snapshot Info",
                    Margin = "10 7",
                    Style = Styles.CreateHeaderStyle()
                };

                infoPanel.AddChild(infoHeader);

                var atrValueSnapshotInfo = CreateInfoTextWithLabel("ATR Value: ", "", AtrValueSnapshotOutputKey);
                infoPanel.AddChild(atrValueSnapshotInfo);

                var atrTrailSnapshotInfo = CreateInfoTextWithLabel("ATR Trail: ", "", TrailAtrSnapshotOutputKey);
                infoPanel.AddChild(atrTrailSnapshotInfo);

                return infoPanel;
            }*/

            private ControlBase CreateInfoHeader()
            {
                var headerBorder = new Border 
                {
                    BorderThickness = "0 0 0 1",
                    Style = Styles.CreateCommonBorderStyle()
                };

                var header = new TextBlock 
                {
                    Text = "Snapshot Info",
                    Margin = "10 7",
                    Style = Styles.CreateHeaderStyle()
                };

                headerBorder.Child = header;
                return headerBorder;
            }

            private StackPanel CreateInfoPanel()
            {
                var infoPanel = new StackPanel 
                {
                    Margin = 10
                };
                var grid = new Grid(1, 3);
                grid.Columns[1].SetWidthInPixels(5);

                var atrValueSnapshotInfo = CreateOutputWithLabel("ATR Value: ", "", AtrValueSnapshotOutputKey);
                grid.AddChild(atrValueSnapshotInfo, 0, 0, 1, 3);

                //var atrTrailSnapshotInfo = CreateOutputWithLabel("ATR Trail: ", "", TrailStopAtrSnapshotOutputKey);
                //grid.AddChild(atrTrailSnapshotInfo, 0, 2);

                infoPanel.AddChild(grid);

                return infoPanel;
            }

            /*private Panel CreateInfoTextWithLabel(string label, string defaultValue, string outputKey)
            {
                var stackPanel = new StackPanel 
                {
                    Orientation = Orientation.Horizontal,
                    Margin = "0 10 0 0"
                };

                var labelTextBlock = new TextBlock 
                {
                    Text = label
                };

                var infoTextBlock = new TextBlock 
                {
                    Text = defaultValue
                };

                _outputMap.Add(outputKey, infoTextBlock);

                stackPanel.AddChild(labelTextBlock);
                stackPanel.AddChild(infoTextBlock);

                return stackPanel;
            }*/

            private void ExecuteOrderAsync(TradeType tradeType)
            {
                // read the input from user
                double volume = GetValueFromInput(VolumeInputKey, 0);
                double stopLossPrice = GetValueFromInput(StopLossPriceInputKey, 0);
                double takeProfitPrice = GetValueFromInput(TakeProfitPriceInputKey, 0);
                double entryPrice = GetValueFromInput(EntryPriceInputKey, 0);
                int expireAfterMinutes = (int)GetValueFromInput(ExpireAfterMinutesInputKey, 0);

                // verify volume
                if (volume == 0)
                {
                    _robot.Print("You must specify Volume in trading panel!");
                    return;
                }
                else if (volume < 2000)
                {
                    _robot.Print("Volume is too small for scale out!");
                    return;
                }

                // get current price and indicator value
                double ask = _robot.Symbol.Ask;
                double bid = _robot.Symbol.Bid;
                double pipSize = _robot.Symbol.PipSize;
                double atr = _averageTrueRange.Result.LastValue;

                // use ATR for stopLoss if stopLossPrice is empty
                if (stopLossPrice == 0)
                {
                    stopLossPrice = tradeType == TradeType.Buy ? ask - atr * _stopLossAtr : bid + atr * _stopLossAtr;
                    // round to correct decimal places
                    stopLossPrice = Math.Round(stopLossPrice, _robot.Symbol.Digits);
                }

                // use ATR for stopLoss if takeProfitPrice is empty
                if (takeProfitPrice == 0)
                {
                    takeProfitPrice = tradeType == TradeType.Buy ? ask + atr * _takeProfitAtr : bid - atr * _takeProfitAtr;
                    // round to correct decimal places
                    takeProfitPrice = Math.Round(takeProfitPrice, _robot.Symbol.Digits);
                }

                // verify take profit and stop loss
                if (tradeType == TradeType.Buy && takeProfitPrice < stopLossPrice)
                {
                    _robot.Print("Take profit price must be above stop loss price in Buy order.");
                    return;
                }
                else if (tradeType == TradeType.Sell && takeProfitPrice > stopLossPrice)
                {
                    _robot.Print("Take profit price must be below stop loss price in Sell order.");
                    return;
                }

                // verify entry price
                if (entryPrice != 0)
                {
                    if (tradeType == TradeType.Buy && (entryPrice < stopLossPrice || entryPrice > takeProfitPrice))
                    {
                        _robot.Print("Entry price must be greater than stop loss price and smaller than take profit price in Sell order.");
                        return;
                    }
                    else if (entryPrice != 0 && tradeType == TradeType.Sell && (entryPrice > stopLossPrice || entryPrice < takeProfitPrice))
                    {
                        _robot.Print("Entry price must be smaller than stop loss price and greater than take profit price in Buy order.");
                        return;
                    }
                }

                // change volume into tradable volume size.
                double volumeHalf = _robot.Symbol.NormalizeVolumeInUnits(volume / 2, RoundingMode.Down);

                DateTime expiry = _robot.Server.Time.AddMinutes(expireAfterMinutes);

                if (entryPrice == 0)
                {
                    double basePrice = tradeType == TradeType.Buy ? ask : bid;
                    double stopLossPips = tradeType == TradeType.Buy ? (ask - stopLossPrice) / pipSize : (stopLossPrice - bid) / pipSize;
                    stopLossPips = Math.Round(stopLossPips, 1);
                    double takeProfitPips = tradeType == TradeType.Buy ? (takeProfitPrice - ask) / pipSize : (bid - takeProfitPrice) / pipSize;
                    takeProfitPips = Math.Round(takeProfitPips, 1);
                    _robot.ExecuteMarketRangeOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, 0, basePrice, label, stopLossPips, takeProfitPips);
                    _robot.ExecuteMarketRangeOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, 0, basePrice, label, stopLossPips, null);
                }
                else
                {
                    if (tradeType == TradeType.Buy)
                    {
                        double stopLossPips = (entryPrice - stopLossPrice) / pipSize;
                        double takeProfitPips = (takeProfitPrice - entryPrice) / pipSize;
                        if (entryPrice <= ask)
                        {
                            _robot.PlaceLimitOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, takeProfitPips, expiry);
                            _robot.PlaceLimitOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, null, expiry);
                        }
                        else
                        {
                            _robot.PlaceStopOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, takeProfitPips, expiry);
                            _robot.PlaceStopOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, null, expiry);
                        }
                    }
                    else
                    {
                        double stopLossPips = (stopLossPrice - entryPrice) / pipSize;
                        double takeProfitPips = (entryPrice - takeProfitPrice) / pipSize;
                        if (entryPrice >= bid)
                        {
                            _robot.PlaceLimitOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, takeProfitPips, expiry);
                            _robot.PlaceLimitOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, null, expiry);
                        }
                        else
                        {
                            _robot.PlaceStopOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, takeProfitPips, expiry);
                            _robot.PlaceStopOrderAsync(tradeType, _robot.Symbol.Name, volumeHalf, entryPrice, label, stopLossPips, null, expiry);
                        }
                    }
                }

                UpdateOutputInfo(AtrValueSnapshotOutputKey, atr.ToString());
                //UpdateOutputInfo(TrailStopAtrSnapshotOutputKey, GetValueFromInput(TrailAtrInputKey, 0).ToString());

            }

            private double GetValueFromInput(string inputKey, double defaultValue)
            {
                double value;
                return double.TryParse(_inputMap[inputKey].Text, out value) ? value : defaultValue;
            }

            private void UpdateOutputInfo(string outputKey, string updateValue)
            {
                _outputMap[outputKey].Text = updateValue;
            }

            public double GetOutputInfoValue(string outputKey)
            {
                double value;
                return double.TryParse(_outputMap[outputKey].Text, out value) ? value : 0;
            }

        }

        public static class Styles
        {
            public static Style CreatePanelBackgroundStyle()
            {
                var style = new Style();
                style.Set(ControlProperty.CornerRadius, 3);
                style.Set(ControlProperty.BackgroundColor, GetColorWithOpacity(Color.FromHex("#292929"), 0.85m), ControlState.DarkTheme);
                style.Set(ControlProperty.BackgroundColor, GetColorWithOpacity(Color.FromHex("#FFFFFF"), 0.85m), ControlState.LightTheme);
                style.Set(ControlProperty.BorderColor, Color.FromHex("#3C3C3C"), ControlState.DarkTheme);
                style.Set(ControlProperty.BorderColor, Color.FromHex("#C3C3C3"), ControlState.LightTheme);
                style.Set(ControlProperty.BorderThickness, new Thickness(1));

                return style;
            }

            public static Style CreateCommonBorderStyle()
            {
                var style = new Style();
                style.Set(ControlProperty.BorderColor, GetColorWithOpacity(Color.FromHex("#FFFFFF"), 0.12m), ControlState.DarkTheme);
                style.Set(ControlProperty.BorderColor, GetColorWithOpacity(Color.FromHex("#000000"), 0.12m), ControlState.LightTheme);
                return style;
            }

            public static Style CreateHeaderStyle()
            {
                var style = new Style();
                style.Set(ControlProperty.ForegroundColor, GetColorWithOpacity("#FFFFFF", 0.70m), ControlState.DarkTheme);
                style.Set(ControlProperty.ForegroundColor, GetColorWithOpacity("#000000", 0.65m), ControlState.LightTheme);
                return style;
            }

            public static Style CreateInputStyle()
            {
                var style = new Style(DefaultStyles.TextBoxStyle);
                style.Set(ControlProperty.BackgroundColor, Color.FromHex("#1A1A1A"), ControlState.DarkTheme);
                style.Set(ControlProperty.BackgroundColor, Color.FromHex("#111111"), ControlState.DarkTheme | ControlState.Hover);
                style.Set(ControlProperty.BackgroundColor, Color.FromHex("#E7EBED"), ControlState.LightTheme);
                style.Set(ControlProperty.BackgroundColor, Color.FromHex("#D6DADC"), ControlState.LightTheme | ControlState.Hover);
                style.Set(ControlProperty.CornerRadius, 3);
                return style;
            }

            public static Style CreateBuyButtonStyle()
            {
                return CreateButtonStyle(Color.FromHex("#009345"), Color.FromHex("#10A651"));
            }

            public static Style CreateSellButtonStyle()
            {
                return CreateButtonStyle(Color.FromHex("#F05824"), Color.FromHex("#FF6C36"));
            }

            public static Style CreateCloseButtonStyle()
            {
                return CreateButtonStyle(Color.FromHex("#F05824"), Color.FromHex("#FF6C36"));
            }

            private static Style CreateButtonStyle(Color color, Color hoverColor)
            {
                var style = new Style(DefaultStyles.ButtonStyle);
                style.Set(ControlProperty.BackgroundColor, color, ControlState.DarkTheme);
                style.Set(ControlProperty.BackgroundColor, color, ControlState.LightTheme);
                style.Set(ControlProperty.BackgroundColor, hoverColor, ControlState.DarkTheme | ControlState.Hover);
                style.Set(ControlProperty.BackgroundColor, hoverColor, ControlState.LightTheme | ControlState.Hover);
                style.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.DarkTheme);
                style.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);
                return style;
            }

            private static Color GetColorWithOpacity(Color baseColor, decimal opacity)
            {
                var alpha = (int)Math.Round(byte.MaxValue * opacity, MidpointRounding.AwayFromZero);
                return Color.FromArgb(alpha, baseColor);
            }
        }

    }
}
