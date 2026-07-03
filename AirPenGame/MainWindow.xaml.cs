using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AirPenGame
{
    public enum GameDirection { None, Right, Left, Top, Bottom }

    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private enum GameState { MainMenu, GameSelection, LeaderboardSelection, Leaderboard, StartScreen, WaitingForGreen, ReadyToClick, ResultScreen }
        private GameState currentState = GameState.MainMenu;

        private int currentVariant = 1;
        private int currentTrial = 0;
        private const int MaxTrials = 5;

        private List<(long time, bool correct)> trialResults = new List<(long time, bool correct)>();
        private List<long> trialTimesOnly = new List<long>();

        private DispatcherTimer waitTimer;
        private Stopwatch stopwatch;
        private Random random;
        private Point screenCenter;

        private GameDirection currentTargetDirection = GameDirection.None;

        private DispatcherTimer aimLabTimer;
        private int aimLabHits = 0;
        private int aimLabMissed = 0;
        private int aimLabTotalClicks = 0;
        private long lastHitTime = 0;

        private readonly SolidColorBrush colorBlue = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B87D1")!);
        private readonly SolidColorBrush colorRed = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CE2636")!);
        private readonly SolidColorBrush colorGreen = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4BCE72")!);

        private long currentFinalScore = 0;
        private string dbFilePath = "scores.csv";

        public MainWindow()
        {
            InitializeComponent();
            random = new Random();
            stopwatch = new Stopwatch();

            waitTimer = new DispatcherTimer();
            waitTimer.Tick += WaitTimer_Tick;

            aimLabTimer = new DispatcherTimer();
            aimLabTimer.Interval = TimeSpan.FromSeconds(30);
            aimLabTimer.Tick += AimLabTimer_Tick;

            ShowMainMenu();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            screenCenter = new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2);
        }

        private void ShowMainMenu()
        {
            currentState = GameState.MainMenu;
            MainGrid.Background = colorBlue;
            MessageText.Text = "";

            MainMenuPanel.Visibility = Visibility.Visible;
            GameSelectionPanel.Visibility = Visibility.Collapsed;
            LeaderboardSelectionPanel.Visibility = Visibility.Collapsed;
            LeaderboardPanel.Visibility = Visibility.Collapsed;
            ArrowContainer.Visibility = Visibility.Collapsed;
            ResultButtonsPanel.Visibility = Visibility.Collapsed;
            SaveScorePopup.Visibility = Visibility.Collapsed;
            AimLabCanvas.Visibility = Visibility.Collapsed;
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            currentState = GameState.GameSelection;
            MainMenuPanel.Visibility = Visibility.Collapsed;
            GameSelectionPanel.Visibility = Visibility.Visible;
        }

        private void BtnLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            currentState = GameState.LeaderboardSelection;
            MainMenuPanel.Visibility = Visibility.Collapsed;
            LeaderboardSelectionPanel.Visibility = Visibility.Visible;
        }

        private void BtnBackToLeaderboardSelection_Click(object sender, RoutedEventArgs e)
        {
            LeaderboardPanel.Visibility = Visibility.Collapsed;
            LeaderboardSelectionPanel.Visibility = Visibility.Visible;
            currentState = GameState.LeaderboardSelection;
        }

        private void BtnBackToMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowMainMenu();
        }

        private void BtnLeaderboardVariant1_Click(object sender, RoutedEventArgs e) => LoadLeaderboard(1);
        private void BtnLeaderboardVariant2_Click(object sender, RoutedEventArgs e) => LoadLeaderboard(2);
        private void BtnLeaderboardVariant3_Click(object sender, RoutedEventArgs e) => LoadLeaderboard(3);

        private void LoadLeaderboard(int variant)
        {
            currentState = GameState.Leaderboard;
            LeaderboardSelectionPanel.Visibility = Visibility.Collapsed;
            LeaderboardPanel.Visibility = Visibility.Visible;
            LeaderboardTitle.Text = "SALA CHWAŁY";

            if (!File.Exists(dbFilePath))
            {
                LeaderboardGrid.ItemsSource = null;
                return;
            }

            var entries = new List<ScoreEntry>();
            var lines = File.ReadAllLines(dbFilePath);

            foreach (var line in lines)
            {
                var parts = line.Split(';');
                if (parts.Length >= 4 && parts[2] == $"Wariant {variant}")
                {
                    if (long.TryParse(parts[1], out long score))
                    {
                        string missed = parts.Length > 4 ? parts[4] : "-";
                        string accuracy = parts.Length > 5 ? parts[5] : "-";

                        entries.Add(new ScoreEntry
                        {
                            Player = parts[0],
                            Score_ms = score,
                            Date = parts[3],
                            Missed = missed,
                            Accuracy = accuracy
                        });
                    }
                }
            }

            entries = entries.OrderBy(e => e.Score_ms).ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Rank = i + 1;
            }

            LeaderboardGrid.ItemsSource = entries;
        }

        private void StartVariant1_Click(object sender, RoutedEventArgs e) { currentVariant = 1; PrepareGame(); }
        private void StartVariant2_Click(object sender, RoutedEventArgs e) { currentVariant = 2; PrepareGame(); }
        private void StartVariant3_Click(object sender, RoutedEventArgs e) { currentVariant = 3; PrepareGame(); }

        private void PrepareGame()
        {
            GameSelectionPanel.Visibility = Visibility.Collapsed;
            trialResults.Clear();
            trialTimesOnly.Clear();
            currentTrial = 0;
            ShowStartScreen();
        }

        private void ShowStartScreen()
        {
            currentState = GameState.StartScreen;
            MainGrid.Background = colorBlue;
            ArrowContainer.Visibility = Visibility.Collapsed;
            ResultButtonsPanel.Visibility = Visibility.Collapsed;
            AimLabCanvas.Visibility = Visibility.Collapsed;

            if (currentVariant == 1)
                MessageText.Text = "Kiedy czerwone tło zmieni się na zielone kliknij tak szybko jak możesz!\n\nKliknij gdziekolwiek aby zacząć.";
            else if (currentVariant == 2)
                MessageText.Text = "Kiedy czerwone tło zmieni się na zielone zapoznaj się ze strzałką i kliknij w odpowiednim kierunku!\n\nKliknij gdziekolwiek aby zacząć.";
            else if (currentVariant == 3)
                MessageText.Text = "Masz 30 sekund aby trafić jak najwięcej celów.\nCele znikną po 4 sekundach więc klikaj szybko ale dokładnie!\n\nKliknij gdziekolwiek aby zacząć.";
        }

        private void MainGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (SaveScorePopup.Visibility == Visibility.Visible) return;

            Point clickPoint = e.GetPosition(MainGrid);

            switch (currentState)
            {
                case GameState.StartScreen:
                    if (currentVariant == 3) StartAimLabPhase();
                    else StartRedPhase();
                    break;

                case GameState.WaitingForGreen:
                    waitTimer.Stop();
                    MainGrid.Background = colorBlue;
                    MessageText.Text = "Za wcześnie!\nKliknij aby spróbować ponownie.";
                    currentState = GameState.StartScreen;
                    break;

                case GameState.ReadyToClick:

                    if (currentVariant == 3)
                    {
                        aimLabTotalClicks++;
                        return;
                    }

                    stopwatch.Stop();
                    long reactionTime = stopwatch.ElapsedMilliseconds;

                    if (currentVariant == 1)
                    {
                        ProcessValidTrial(reactionTime, true);
                    }
                    else if (currentVariant == 2)
                    {
                        if (IsValidDirection(clickPoint)) ProcessValidTrial(reactionTime, true);
                        else ProcessInvalidTrial();
                    }
                    break;
            }
        }

        private void ProcessValidTrial(long time, bool correct)
        {
            trialResults.Add((time, correct));
            trialTimesOnly.Add(time);
            currentTrial++;

            if (currentTrial < MaxTrials)
            {
                MainGrid.Background = colorBlue;
                ArrowContainer.Visibility = Visibility.Collapsed;
                MessageText.Text = $"{time} ms\n\nKliknij aby kontynuować ({currentTrial}/{MaxTrials})";
                currentState = GameState.StartScreen;
            }
            else
            {
                ShowFinalResults();
            }
        }

        private void ProcessInvalidTrial()
        {
            MainGrid.Background = colorRed;
            ArrowContainer.Visibility = Visibility.Collapsed;
            MessageText.Text = "ZŁY KIERUNEK!\nKliknij, aby ponowić tę próbę.";
            currentState = GameState.StartScreen;
        }

        private void StartRedPhase()
        {
            currentState = GameState.WaitingForGreen;
            MainGrid.Background = colorRed;
            ArrowContainer.Visibility = Visibility.Collapsed;
            MessageText.Text = "Czekaj na zielony...";

            int waitTimeMs = random.Next(1500, 4500);
            waitTimer.Interval = TimeSpan.FromMilliseconds(waitTimeMs);
            waitTimer.Start();
        }

        private void WaitTimer_Tick(object? sender, EventArgs e)
        {
            waitTimer.Stop();
            currentState = GameState.ReadyToClick;
            MainGrid.Background = colorGreen;
            MessageText.Text = "RUCH!";

            if (currentVariant == 2)
            {
                SetupDirectionalArrow();
                CenterMouseCursor();
            }

            stopwatch.Restart();
        }

        private void SetupDirectionalArrow()
        {
            var values = Enum.GetValues(typeof(GameDirection)).Cast<GameDirection>().Where(d => d != GameDirection.None).ToList();
            currentTargetDirection = values[random.Next(values.Count)];

            switch (currentTargetDirection)
            {
                case GameDirection.Right: ArrowRotateTransform.Angle = 0; break;
                case GameDirection.Left: ArrowRotateTransform.Angle = 180; break;
                case GameDirection.Top: ArrowRotateTransform.Angle = -90; break;
                case GameDirection.Bottom: ArrowRotateTransform.Angle = 90; break;
            }

            ArrowContainer.Visibility = Visibility.Visible;
        }

        private void CenterMouseCursor()
        {
            screenCenter = MainGrid.PointToScreen(new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2));
            SetCursorPos((int)screenCenter.X, (int)screenCenter.Y);
        }

        private bool IsValidDirection(Point clickPoint)
        {
            Point gridCenter = new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2);
            Vector moveVector = Point.Subtract(clickPoint, gridCenter);

            if (moveVector.Length < 10) return false;

            double angleInRadians = Math.Atan2(moveVector.Y, moveVector.X);
            double angleInDegrees = angleInRadians * (180 / Math.PI);

            switch (currentTargetDirection)
            {
                case GameDirection.Right: return angleInDegrees >= -45 && angleInDegrees <= 45;
                case GameDirection.Left: return angleInDegrees >= 135 || angleInDegrees <= -135;
                case GameDirection.Top: return angleInDegrees >= -135 && angleInDegrees <= -45;
                case GameDirection.Bottom: return angleInDegrees >= 45 && angleInDegrees <= 135;
                default: return false;
            }
        }

        private void StartAimLabPhase()
        {
            currentState = GameState.ReadyToClick;
            MainGrid.Background = colorBlue;
            MessageText.Text = "";
            AimLabCanvas.Visibility = Visibility.Visible;
            AimLabCanvas.Children.Clear();

            aimLabHits = 0;
            aimLabTotalClicks = 0;
            trialTimesOnly.Clear();

            stopwatch.Restart();
            lastHitTime = stopwatch.ElapsedMilliseconds;

            for (int i = 0; i < 5; i++)
            {
                SpawnTarget();
            }

            aimLabTimer.Start();
        }

        private void SpawnTarget()
        {
            double size = random.Next(80, 130);
            double maxX = MainGrid.ActualWidth - size - 20;
            double maxY = MainGrid.ActualHeight - size - 20;

            if (maxX <= 0 || maxY <= 0) return;

            double posX, posY;
            Rect newRect;
            bool isOverlapping;
            int attempts = 0;

            do
            {
                isOverlapping = false;
                posX = random.NextDouble() * maxX;
                posY = random.NextDouble() * maxY;
                newRect = new Rect(posX, posY, size, size);

                foreach (UIElement child in AimLabCanvas.Children)
                {
                    if (child is Ellipse existing)
                    {
                        Rect existingRect = new Rect(Canvas.GetLeft(existing), Canvas.GetTop(existing), existing.Width, existing.Height);
                        if (newRect.IntersectsWith(existingRect)) { isOverlapping = true; break; }
                    }
                }
                attempts++;
            } while (isOverlapping && attempts < 50);

            Ellipse target = new Ellipse { Width = size, Height = size, Fill = colorRed, Cursor = Cursors.Hand };
            target.MouseLeftButtonDown += Target_Click;

            DispatcherTimer expireTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            expireTimer.Tick += (s, args) => {
                expireTimer.Stop();
                if (AimLabCanvas.Children.Contains(target))
                {
                    AimLabCanvas.Children.Remove(target);
                    aimLabMissed++;
                    SpawnTarget();
                }
            };
            target.Tag = expireTimer;
            expireTimer.Start();

            Canvas.SetLeft(target, posX);
            Canvas.SetTop(target, posY);
            AimLabCanvas.Children.Add(target);
        }

        private void Target_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is Ellipse target)
            {
                if (target.Tag is DispatcherTimer timer) timer.Stop();

                aimLabHits++;
                aimLabTotalClicks++;
                long currentTime = stopwatch.ElapsedMilliseconds;
                trialTimesOnly.Add(currentTime - lastHitTime);
                lastHitTime = currentTime;

                AimLabCanvas.Children.Remove(target);
                SpawnTarget();
            }
        }

        private void AimLabTimer_Tick(object? sender, EventArgs e)
        {
            aimLabTimer.Stop();
            stopwatch.Stop();

            AimLabCanvas.Visibility = Visibility.Collapsed;
            AimLabCanvas.Children.Clear();

            ShowFinalResultsAimLab();
        }

        private void ShowFinalResultsAimLab()
        {
            currentState = GameState.ResultScreen;
            MainGrid.Background = colorBlue;

            long median = 0;
            if (trialTimesOnly.Count > 0)
            {
                var sortedResults = trialTimesOnly.OrderBy(x => x).ToList();
                median = sortedResults[trialTimesOnly.Count / 2];
            }

            currentFinalScore = median;
            double accuracy = 0;

            if (aimLabTotalClicks > 0)
            {
                accuracy = Math.Round((double)aimLabHits / aimLabTotalClicks * 100, 1);
            }

            MessageText.Text = $"Koniec Czasu!\n\nTrafienia: {aimLabHits}/{aimLabTotalClicks} ({accuracy}%)\nMEDIANA CZASU MIĘDZY TRAFIENIAMI: {median} ms";
            ResultButtonsPanel.Visibility = Visibility.Visible;
        }

        private void ShowFinalResults()
        {
            currentState = GameState.ResultScreen;
            MainGrid.Background = colorBlue;
            ArrowContainer.Visibility = Visibility.Collapsed;

            var sortedResults = trialTimesOnly.OrderBy(x => x).ToList();
            currentFinalScore = sortedResults[2];

            string allTimes = string.Join(" ms, ", trialTimesOnly) + " ms";
            MessageText.Text = $"Koniec!\n\nTwoje czasy: {allTimes}\n\nMEDIANA: {currentFinalScore} ms";

            ResultButtonsPanel.Visibility = Visibility.Visible;
        }

        private void BtnReturnFromGame_Click(object sender, RoutedEventArgs e)
        {
            ResultButtonsPanel.Visibility = Visibility.Collapsed;
            currentState = GameState.GameSelection;
            GameSelectionPanel.Visibility = Visibility.Visible;
            MessageText.Text = "";
        }

        private void BtnShowSavePopup_Click(object sender, RoutedEventArgs e)
        {
            PlayerNameTextBox.Text = "";
            SaveScorePopup.Visibility = Visibility.Visible;
        }

        private void BtnCancelSave_Click(object sender, RoutedEventArgs e)
        {
            SaveScorePopup.Visibility = Visibility.Collapsed;
        }

        private void BtnConfirmSave_Click(object sender, RoutedEventArgs e)
        {
            string playerName = PlayerNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(playerName))
            {
                MessageBox.Show("Podaj jakąś nazwę!");
                return;
            }

            SaveScore(playerName, currentFinalScore, currentVariant);

            SaveScorePopup.Visibility = Visibility.Collapsed;
            ResultButtonsPanel.Visibility = Visibility.Collapsed;

            LoadLeaderboard(currentVariant);
            MessageText.Text = "";
        }

        private void SaveScore(string username, long score, int variant)
        {
            try
            {
                string extraData = "-;-";
                if (variant == 3)
                {
                    float acc = aimLabTotalClicks > 0 ? (float)Math.Round((float)aimLabHits / aimLabTotalClicks * 100, 1) : 0f;
                    extraData = $"{aimLabMissed};{acc}";
                }

                using (StreamWriter sw = File.AppendText(dbFilePath))
                {
                    string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    sw.WriteLine($"{username};{score};Wariant {variant};{date};{extraData}");
                }
            }
            catch (Exception ex) { MessageBox.Show("Błąd zapisu: " + ex.Message); }
        }
    }

    public class ScoreEntry
    {
        public int Rank { get; set; }
        public string Player { get; set; } = string.Empty;
        public long Score_ms { get; set; }
        public string Date { get; set; } = string.Empty;
        public string Missed { get; set; } = "-";
        public string Accuracy { get; set; } = "-";
    }
}