using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AirPenGame
{
    public enum GameDirection { None, Right, Left, Top, Bottom }

    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private enum GameState { Menu, StartScreen, WaitingForGreen, ReadyToClick, ResultScreen }
        private GameState currentState = GameState.Menu;

        private int currentVariant = 1;
        private int currentTrial = 0;
        private const int MaxTrials = 5;

        private List<(long time, bool correct)> trialResults = new List<(long time, bool correct)>();
        private List<long> trialTimesOnly = new List<long>();

        private DispatcherTimer waitTimer;
        private Stopwatch stopwatch;
        private Random random;

        private GameDirection currentTargetDirection = GameDirection.None;
        private Point screenCenter;

        private readonly SolidColorBrush colorBlue = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B87D1")!);
        private readonly SolidColorBrush colorRed = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CE2636")!);
        private readonly SolidColorBrush colorGreen = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4BCE72")!);

        public MainWindow()
        {
            InitializeComponent();
            random = new Random();
            stopwatch = new Stopwatch();

            waitTimer = new DispatcherTimer();
            waitTimer.Tick += WaitTimer_Tick;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            screenCenter = new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2);
        }

        private void StartVariant1_Click(object sender, RoutedEventArgs e)
        {
            currentVariant = 1;
            ResetGame();
            ShowStartScreen();
        }

        private void StartVariant2_Click(object sender, RoutedEventArgs e)
        {
            currentVariant = 2;
            ResetGame();
            ShowStartScreen();
        }

        private void ResetGame()
        {
            trialResults.Clear();
            trialTimesOnly.Clear();
            currentTrial = 0;
            MenuPanel.Visibility = Visibility.Collapsed;
            ArrowContainer.Visibility = Visibility.Collapsed;
        }

        private void ShowStartScreen()
        {
            currentState = GameState.StartScreen;
            MainGrid.Background = colorBlue;
            ArrowContainer.Visibility = Visibility.Collapsed;

            if (currentVariant == 1)
                MessageText.Text = "Kiedy czerwone tło zmieni się na zielone kliknij tak szybko jak możesz!\n\nKliknij gdziekolwiek aby zacząć.";
            else
                MessageText.Text = "Kiedy czerwone tło zmieni się na zielone zapoznaj się ze strzałką i kliknij w odpowiednim kierunku!\n\nKliknij gdziekolwiek aby zacząć.";
        }

        private void MainGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point clickPoint = e.GetPosition(MainGrid);

            switch (currentState)
            {
                case GameState.StartScreen:
                    StartRedPhase();
                    break;

                case GameState.WaitingForGreen:
                    waitTimer.Stop();
                    MainGrid.Background = colorBlue;
                    MessageText.Text = "Za wcześnie!\nKliknij aby spróbować ponownie.";
                    currentState = GameState.StartScreen;
                    break;

                case GameState.ReadyToClick:
                    stopwatch.Stop();
                    long reactionTime = stopwatch.ElapsedMilliseconds;

                    if (currentVariant == 1)
                    {
                        ProcessValidTrial(reactionTime, true);
                    }
                    else if (currentVariant == 2)
                    {
                        if (IsValidDirection(clickPoint))
                        {
                            ProcessValidTrial(reactionTime, true);
                        }
                        else
                        {
                            ProcessInvalidTrial();
                        }
                    }
                    break;

                case GameState.ResultScreen:
                    currentState = GameState.Menu;
                    MainGrid.Background = colorBlue;
                    MessageText.Text = "AirPen Reflex Trainer";
                    MenuPanel.Visibility = Visibility.Visible;
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
                case GameDirection.Right:
                    return angleInDegrees >= -45 && angleInDegrees <= 45;
                case GameDirection.Left:
                    return angleInDegrees >= 135 || angleInDegrees <= -135;
                case GameDirection.Top:
                    return angleInDegrees >= -135 && angleInDegrees <= -45;
                case GameDirection.Bottom:
                    return angleInDegrees >= 45 && angleInDegrees <= 135;
                default:
                    return false;
            }
        }

        private void ShowFinalResults()
        {
            currentState = GameState.ResultScreen;
            MainGrid.Background = colorBlue;
            ArrowContainer.Visibility = Visibility.Collapsed;

            var sortedResults = trialTimesOnly.OrderBy(x => x).ToList();
            long median = sortedResults[2];

            string allTimes = string.Join(" ms, ", trialTimesOnly) + " ms";

            MessageText.Text = $"Koniec!\n\nTwoje czasy: {allTimes}\n\nMEDIANA: {median} ms\n\nKliknij aby wrócić do menu.";
        }
    }
}