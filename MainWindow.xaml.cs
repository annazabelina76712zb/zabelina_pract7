using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace pr7_trpo_praktika
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private const string Route = "api/v1/Movies";
        private readonly HttpClient hc = new HttpClient
        {
            BaseAddress = new Uri("https://localhost:7193/"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private Movie? openedMovie;
        private bool busy;
        public bool IsNotBusy => !busy;
        public ObservableCollection<Movie> Movies { get; } = new();
        public Movie? SelectedMovie { get; set; }
        public string MovieTitle { get; set; } = "";
        public string MovieGenre { get; set; } = "";
        public string DurationText { get; set; } = "0";
        public string ReleaseYearText { get; set; } = "";
        public bool MovieIsAvailable { get; set; }
        public string FilterGenre { get; set; } = "";
        public string FilterMinYear { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) => await RunAsync(GetItems);
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(GetItems);
        private async void Apply_Click(object sender, RoutedEventArgs e) => await RunAsync(GetItems);

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            FilterGenre = "";
            FilterMinYear = "";
            Notify();
            await RunAsync(GetItems);
        }

        private async Task GetItems()
        {
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(FilterGenre))
                filters.Add("genre=" + Uri.EscapeDataString(FilterGenre.Trim()));
            if (!string.IsNullOrWhiteSpace(FilterMinYear))
            {
                if (!int.TryParse(FilterMinYear, out int minYear) || minYear < 0)
                    throw new Exception("Минимальный год должен быть неотрицательным целым числом.");
                filters.Add("minYear=" + minYear);
            }

            var url = Route + (filters.Count == 0 ? "" : "?" + string.Join("&", filters));
            var movies = await hc.GetFromJsonAsync<List<Movie>>(url) ?? throw new Exception("API вернул пустой ответ.");
            ClearForm();
            Movies.Clear();
            foreach (var movie in movies) Movies.Add(movie);
        }

        private async void Movies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (busy || SelectedMovie == null) return;
            int id = SelectedMovie.Id;

            FillForm(null);
            await RunAsync(async () =>
            {
                var movie = await hc.GetFromJsonAsync<Movie>($"{Route}/{id}") ?? throw new Exception("API вернул пустую запись.");
                FillForm(movie);
            });
        }

        private void FillForm(Movie? movie)
        {
            openedMovie = movie;
            MovieTitle = movie?.Title ?? "";
            MovieGenre = movie?.Genre ?? "";
            DurationText = movie?.DurationMinutes.ToString() ?? "0";
            ReleaseYearText = movie?.ReleaseYear.ToString() ?? "0";
            MovieIsAvailable = movie?.IsAvailable ?? false;
            Notify();
        }

        private void ClearForm()
        {
            SelectedMovie = null;
            FillForm(null);
        }

        private Movie ReadForm()
        {
            if (string.IsNullOrWhiteSpace(MovieTitle) || string.IsNullOrWhiteSpace(MovieGenre))
                throw new Exception("Заполните название и жанр фильма.");
            if (!int.TryParse(DurationText, out int duration) || duration <= 0)
                throw new Exception("Длительность должна быть положительным целым числом.");
            if (!int.TryParse(ReleaseYearText, out int releaseYear) || releaseYear <= 0)
                throw new Exception("Год выпуска должен быть положительным целым числом.");

            return new Movie
            {
                Title = MovieTitle.Trim(),
                Genre = MovieGenre.Trim(),
                DurationMinutes = duration,
                ReleaseYear = releaseYear,
                IsAvailable = MovieIsAvailable
            };
        }

        private Movie RequireMovie() => openedMovie ?? throw new Exception("Сначала выберите фильм в списке.");

        private async void Create_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            using var response = await hc.PostAsJsonAsync(Route, ReadForm());
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async void ChangeFull_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            int id = RequireMovie().Id;
            var movie = ReadForm();
            movie.Id = id;
            using var response = await hc.PutAsJsonAsync($"{Route}/{id}", movie);
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async void Change_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            var old = RequireMovie();
            var movie = ReadForm();
            var changes = new Dictionary<string, object>();
            if (movie.Title != old.Title) changes["title"] = movie.Title;
            if (movie.Genre != old.Genre) changes["genre"] = movie.Genre;
            if (movie.DurationMinutes != old.DurationMinutes) changes["durationMinutes"] = movie.DurationMinutes;
            if (movie.ReleaseYear != old.ReleaseYear) changes["releaseYear"] = movie.ReleaseYear;
            if (movie.IsAvailable != old.IsAvailable) changes["isAvailable"] = movie.IsAvailable;
            if (changes.Count == 0) { MessageBox.Show("Нет изменений."); return; }

            using var content = JsonContent.Create(changes);
            using var response = await hc.PatchAsync($"{Route}/{old.Id}", content);
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async void Del_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            int id = RequireMovie().Id;
            if (MessageBox.Show("Удалить выбранный фильм?", "Удаление",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            using var response = await hc.DeleteAsync($"{Route}/{id}");
            response.EnsureSuccessStatusCode();
            await RefreshAfterChange();
        });

        private async Task RefreshAfterChange()
        {
            ClearForm();
            try { await GetItems(); }
            catch { throw new Exception("Операция выполнена, но список не обновился. Нажмите «Обновить» после восстановления связи."); }
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            Notify();
            try { await action(); }
            catch (HttpRequestException ex)
            {
                MessageBox.Show(ex.StatusCode == null
                    ? "Сервер недоступен. Проверьте, что API запущен и адрес указан верно."
                    : $"Ошибка API: {(int) ex.StatusCode.Value}. {ex.Message}");
            }
            catch (TaskCanceledException) { MessageBox.Show("Сервер не ответил за 15 секунд."); }
            catch (JsonException) { MessageBox.Show("API вернул данные неверного формата."); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { busy = false; Notify(); }
        }
    }

    public class Movie
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Genre { get; set; } = "";
        public int DurationMinutes { get; set; }
        public int ReleaseYear { get; set; }
        public bool IsAvailable { get; set; }
    }
}
