using OrbitClient.Scripts;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace OrbitClient;

public partial class MainWindow
{
    private OrbitAccountService? orbitAccount;
    private OrbitAccountConfig orbitConfig = new();
    private OrbitProfile? orbitProfile;
    private ImageSource? orbitAvatar;
    private TextBlock orbitStatus = null!;
    private StackPanel orbitForm = null!;
    private Button orbitCancel = null!;
    private CancellationTokenSource? orbitSignInCancel;
    private bool orbitBusy;
    private bool orbitRegister;
    private string orbitEmail = "";
    private bool orbitRemember = true;
    private bool orbitConfirmationPending;
    private string? orbitDraftUsername;
    private string? orbitDraftBio;
    private string OrbitConfigPath => Path.Combine(workspace.DataRoot, "account-connection.json");

    private void InitializeOrbitAccounts()
    {
        string warning = "";
        try
        {
            orbitConfig = OrbitAccountConfig.Load(OrbitConfigPath);
            orbitAccount = new OrbitAccountService(orbitConfig);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            orbitConfig = new();
            orbitAccount = new OrbitAccountService(orbitConfig);
            warning = "Using Orbit's default connection because the saved configuration could not be loaded.";
        }
        BuildOrbitAccount(warning);
        Closed += (_, _) =>
        {
            orbitSignInCancel?.Cancel();
            orbitAccount?.Dispose();
        };
    }

    private async Task RestoreOrbitAccountAsync()
    {
        if (orbitAccount is null || orbitBusy) return;
        await RunOrbitAction(async () =>
        {
            if (await orbitAccount.RestoreAsync())
            {
                var welcome = orbitAccount.StorageWarning ?? "Welcome back.";
                try
                {
                    await LoadOrbitProfileAsync();
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException)
                {
                    welcome = "Signed in. Profile details are unavailable; try Reload profile.";
                }
                BuildOrbitAccount(welcome);
            }
            else BuildOrbitAccount();
        }, "Checking saved sign-in…");
    }

    private TextBox OrbitInput(string label, StackPanel parent, string value = "", int maximum = 320)
    {
        parent.Children.Add(FeatureText(label, 12, true));
        var input = new TextBox
        {
            Text = value,
            MaxLength = maximum,
            Padding = new Thickness(12, 9, 12, 9),
            Background = new SolidColorBrush(Color.FromRgb(28, 21, 35)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(73, 53, 87)),
            CaretBrush = Brushes.White,
            Margin = new Thickness(0, 0, 0, 14),
            FontSize = 14
        };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        parent.Children.Add(input);
        return input;
    }

    private Button OrbitButton(string label, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource(primary ? "Primary" : "Ghost"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await RunOrbitAction(action);
        return button;
    }

    private async Task RunOrbitAction(Func<Task> action, string message = "Working…")
    {
        if (orbitBusy) return;
        var wasSignedIn = orbitAccount?.Account is not null;
        orbitBusy = true;
        orbitForm.IsEnabled = false;
        orbitStatus.Text = message;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            orbitStatus.Text = "Sign-in cancelled or timed out. You can try again.";
        }
        catch (HttpRequestException)
        {
            orbitStatus.Text = "Could not reach the account service. Check your internet connection.";
        }
        catch (IOException ex)
        {
            orbitStatus.Text = ex.Message;
        }
        catch (Exception)
        {
            orbitStatus.Text = "The account action could not finish. Please try again.";
        }
        finally
        {
            orbitBusy = false;
            if (!windowClosed)
            {
            orbitForm.IsEnabled = true;
            orbitCancel.Visibility = Visibility.Collapsed;
            if (orbitAccount?.Account is null && (orbitProfile is not null || wasSignedIn))
            {
                orbitProfile = null;
                orbitAvatar = null;
                orbitDraftUsername = null;
                orbitDraftBio = null;
                BuildOrbitAccount(orbitStatus.Text);
            }
            else if (!wasSignedIn && orbitAccount?.Account is not null) BuildOrbitAccount(orbitStatus.Text);
            UpdateProfileNav();
            }
        }
    }

    private void BuildOrbitAccount(string message = "")
    {
        if (windowClosed) return;
        UpdateProfileNav();
        OrbitAccountHost.Children.Clear();
        var card = new StackPanel();
        card.Children.Add(FeatureText(orbitAccount?.Account is null ? orbitRegister ? "Create your account" : "Welcome back" : "Your profile", 26));
        card.Children.Add(FeatureText(orbitAccount?.Account is null ? "Make yourself at home." : "Keep it personal.", 13, true));
        orbitStatus = FeatureText(message, 13, true);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(orbitStatus, System.Windows.Automation.AutomationLiveSetting.Polite);
        card.Children.Add(orbitStatus);
        orbitForm = new StackPanel
        {
            MaxWidth = 620,
            Width = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 320,
            IsEnabled = !orbitBusy
        };
        card.Children.Add(orbitForm);
        if (orbitAccount?.Account is { }
          account)
        {
            var identity = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };
            identity.Children.Add(ProfileAvatar(72));
            var identityText = new StackPanel
            {
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            identityText.Children.Add(FeatureText(ProfileName(), 22));
            identityText.Children.Add(FeatureText("Signed in", 12, true));
            identity.Children.Add(identityText);
            orbitForm.Children.Add(identity);
            orbitForm.Children.Add(FeatureText(account.Email, 13, true));
            var pictureActions = new WrapPanel();
            pictureActions.Children.Add(OrbitButton("Change picture", ChangeOrbitPictureAsync));
            pictureActions.Children.Add(OrbitButton("Use default picture", async () =>
            {
                await RequireOrbitAccount().RemoveAvatarAsync();
                orbitAvatar = null;
                await LoadOrbitAvatarAsync();
                BuildOrbitAccount("Profile picture reset.");
            }));
            orbitForm.Children.Add(pictureActions);
            var username = OrbitInput("Username", orbitForm, orbitDraftUsername ?? orbitProfile?.Username ?? "", 24);
            var bio = OrbitInput("Bio", orbitForm, orbitDraftBio ?? orbitProfile?.Bio ?? "", 500);
            bio.AcceptsReturn = true;
            bio.Height = 85;
            bio.TextWrapping = TextWrapping.Wrap;
            var draftStatus = FeatureText("", 12, true);
            orbitForm.Children.Add(draftStatus);
            var row = new WrapPanel();
            var save = OrbitButton(orbitProfile is null ? "Create profile" : "Save profile", async () =>
            {
                orbitProfile = await orbitAccount.SaveProfileAsync(username.Text.Trim().ToLowerInvariant(), bio.Text, orbitProfile is null);
                orbitDraftUsername = null;
                orbitDraftBio = null;
                BuildOrbitAccount("Profile saved.");
            }, true);
            void UpdateDraft()
            {
                orbitDraftUsername = username.Text;
                orbitDraftBio = bio.Text;
                var valid = System.Text.RegularExpressions.Regex.IsMatch(username.Text.Trim().ToLowerInvariant(), "^[a-z0-9_]{3,24}$");
                var changed = orbitProfile is null || username.Text.Trim().ToLowerInvariant() != orbitProfile.Username || bio.Text != orbitProfile.Bio;
                save.IsEnabled = valid && changed;
                draftStatus.Text = !valid ? "Use 3–24 letters, numbers, or underscores." : $"{bio.Text.Length}/500" + (changed ? " · Unsaved changes" : "");
            }
            username.TextChanged += (_, _) => UpdateDraft();
            bio.TextChanged += (_, _) => UpdateDraft();
            UpdateDraft();
            row.Children.Add(save);
            row.Children.Add(OrbitButton("Reload profile", async () =>
            {
                if (!ConfirmDiscardProfile()) return;
                await LoadOrbitProfileAsync();
                orbitDraftUsername = null;
                orbitDraftBio = null;
                BuildOrbitAccount("Profile loaded.");
            }));
            orbitForm.Children.Add(row);
            var logout = new WrapPanel();
            logout.Children.Add(OrbitButton("Sign out", async () => { if (ConfirmDiscardProfile()) await SignOutOrbit(false); }));
            logout.Children.Add(OrbitButton("Sign out all devices", async () =>
            {
                if (MessageBox.Show(this, "Sign out of Orbit on every device?", "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes
                    && ConfirmDiscardProfile()) await SignOutOrbit(true);
            }));
            orbitForm.Children.Add(new Expander { Header = "Session", Foreground = Brushes.White, Content = logout, Margin = new Thickness(0, 16, 0, 0) });
        }
        else
        {
            var email = OrbitInput("Email address", orbitForm, orbitEmail);
            email.TextChanged += (_, _) => orbitEmail = email.Text.Trim();
            var password = new PasswordBox
            {
                Padding = new Thickness(12, 9, 12, 9),
                MaxLength = 512,
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(28, 21, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(73, 53, 87)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            orbitForm.Children.Add(FeatureText(orbitRegister ? "Password (at least 12 characters)" : "Password", 12, true));
            System.Windows.Automation.AutomationProperties.SetName(password, "Password");
            orbitForm.Children.Add(password);
            PasswordBox? confirmation = null;
            if (orbitRegister)
            {
                orbitForm.Children.Add(FeatureText("Confirm password", 12, true));
                confirmation = new PasswordBox
                {
                    Padding = new Thickness(12, 9, 12, 9),
                    MaxLength = 512,
                    FontSize = 14,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(28, 21, 35)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(73, 53, 87)),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                System.Windows.Automation.AutomationProperties.SetName(confirmation, "Confirm password");
                orbitForm.Children.Add(confirmation);
            }
            var remember = new CheckBox
            {
                Content = "Keep me signed in",
                IsChecked = orbitRemember,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 18)
            };
            orbitForm.Children.Add(remember);
            remember.Checked += (_, _) => orbitRemember = true;
            remember.Unchecked += (_, _) => orbitRemember = false;
            var actions = new WrapPanel();
            var signIn = OrbitButton(orbitRegister ? "Create account" : "Sign in", async () =>
            {
                var service = RequireOrbitAccount();
                orbitEmail = email.Text.Trim();
                orbitRemember = remember.IsChecked == true;
                var secret = password.Password;
                try
                {
                    if (orbitRegister)
                    {
                        if (secret != confirmation!.Password) throw new IOException("The passwords do not match.");
                        await service.SignUpAsync(orbitEmail, secret);
                        orbitRegister = false;
                        orbitConfirmationPending = true;
                        BuildOrbitAccount("Check your email to finish signing up.");
                    }
                    else
                    {
                        await service.SignInAsync(orbitEmail, secret, orbitRemember);
                        var status = service.StorageWarning ?? "Signed in. Choose a username to finish your profile.";
                        try
                        {
                            await LoadOrbitProfileAsync();
                        }
                        catch (IOException)
                        {
                            status = "Signed in. Your profile is unavailable. Try Reload profile.";
                        }
                        BuildOrbitAccount(status);
                    }
                }
                finally
                {
                    password.Clear();
                    confirmation?.Clear();
                }
            }, true);
            actions.Children.Add(signIn);
            password.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || orbitBusy) return;
                e.Handled = true;
                signIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            actions.Children.Add(OrbitButton(orbitRegister ? "I already have an account" : "Create an account", () =>
            {
                orbitEmail = email.Text.Trim();
                orbitRemember = remember.IsChecked == true;
                orbitRegister = !orbitRegister;
                BuildOrbitAccount();
                return Task.CompletedTask;
            }));
            orbitForm.Children.Add(actions);
            var discord = OrbitButton("Continue with Discord", async () =>
            {
                var service = RequireOrbitAccount();
                orbitRemember = remember.IsChecked == true;
                orbitSignInCancel?.Dispose();
                orbitSignInCancel = new();
                orbitCancel.Visibility = Visibility.Visible;
                orbitStatus.Text = "Finish Discord sign-in in your browser. Orbit is waiting…";
                await service.SignInWithDiscordAsync(orbitRemember, orbitSignInCancel.Token);
                var status = service.StorageWarning ?? "Signed in with Discord.";
                try
                {
                    await LoadOrbitProfileAsync();
                }
                catch (IOException)
                {
                    status += " Profile unavailable. Try Reload profile.";
                }
                BuildOrbitAccount(status);
            });
            discord.Background = new SolidColorBrush(Color.FromRgb(88, 101, 242));
            orbitForm.Children.Add(FeatureText("or", 12, true));
            orbitForm.Children.Add(discord);
            var verification = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            verification.Children.Add(FeatureText("Paste the link or code from your latest email.", 12, true));
            var emailCode = OrbitInput("Confirmation link or code", verification, maximum: 4096);
            verification.Children.Add(OrbitButton("Verify email and sign in", async () =>
            {
                orbitEmail = email.Text.Trim();
                orbitRemember = remember.IsChecked == true;
                try
                {
                    await RequireOrbitAccount().VerifyEmailAsync(orbitEmail, emailCode.Text, orbitRemember);
                    orbitConfirmationPending = false;
                    var verified = "Email confirmed. Choose a username to finish your profile.";
                    try
                    {
                        await LoadOrbitProfileAsync();
                    }
                    catch (IOException)
                    {
                        verified = "Email confirmed and signed in. Profile details are unavailable; try Reload profile.";
                    }
                    BuildOrbitAccount(orbitAccount?.StorageWarning ?? verified);
                }
                finally
                {
                    emailCode.Clear();
                    password.Clear();
                    confirmation?.Clear();
                }
            }));
            var help = new WrapPanel();
            help.Children.Add(OrbitButton("Forgot password", async () =>
            {
                await RequireOrbitAccount().ResetPasswordAsync(email.Text.Trim());
                orbitStatus.Text = "If an account exists, reset instructions have been sent.";
            }));
            verification.Children.Add(OrbitButton("Resend confirmation", async () =>
            {
                await RequireOrbitAccount().ResendConfirmationAsync(email.Text.Trim());
                orbitEmail = email.Text.Trim();
                orbitConfirmationPending = true;
                orbitStatus.Text = "Check your latest email, then paste the confirmation link or code.";
            }));
            orbitForm.Children.Add(help);
            orbitForm.Children.Add(new Expander { Header = "Confirm email", IsExpanded = orbitConfirmationPending,
                Foreground = Brushes.White, Content = verification, Margin = new Thickness(0, 12, 0, 0) });
        }

        orbitCancel = new Button
        {
            Content = "Cancel Discord sign-in",
            Style = (Style)FindResource("Ghost"),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        orbitCancel.Click += (_, _) => orbitSignInCancel?.Cancel();
        card.Children.Add(orbitCancel);
        OrbitAccountHost.Children.Add(FeatureCard(card));
    }

    private OrbitAccountService RequireOrbitAccount() => orbitAccount ??
      throw new IOException("The account service is unavailable. Restart Orbit and try again.");

    private bool ConfirmDiscardProfile()
    {
        var changed = orbitDraftUsername is not null && (orbitDraftUsername.Trim().ToLowerInvariant() != (orbitProfile?.Username ?? "") || orbitDraftBio != (orbitProfile?.Bio ?? ""));
        return !changed || MessageBox.Show(this, "Discard your unsaved profile changes?", "Profile", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private string ProfileName() => !string.IsNullOrWhiteSpace(orbitProfile?.Username) ? orbitProfile.Username :
      !string.IsNullOrWhiteSpace(orbitAccount?.Account?.DisplayName) ? orbitAccount.Account.DisplayName :
      orbitAccount?.Account is { }
    account ? account.Email.Split('@')[0] : "Profile";

    private FrameworkElement ProfileAvatar(double size)
    {
        if (orbitAvatar is not null)
            return new System.Windows.Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Fill = new ImageBrush(orbitAvatar)
                {
                    Stretch = Stretch.UniformToFill
                }
            };
        var name = ProfileName();
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[..1])).ToUpperInvariant();
        var avatar = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = (Brush)FindResource("AccentSoft")
        };
        avatar.Child = new TextBlock
        {
            Text = initials,
            FontSize = size * .35,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return avatar;
    }

    private void UpdateProfileNav()
    {
        ObserveCapeAccount();
        if (ProfileNav is null) return;
        var name = ProfileName();
        var signedIn = orbitAccount?.Account is not null;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(ProfileAvatar(32));
        if (preferences.ShowNavLabels)
        {
            var text = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Children.Add(new TextBlock
            {
                Text = name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = FontWeights.SemiBold
            });
            text.Children.Add(new TextBlock
            {
                Text = signedIn ? "Orbit profile" : "Sign in",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(169, 154, 178))
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
        }
        ProfileNav.Content = row;
        ProfileNav.ToolTip = signedIn ? "Open " + name + "'s profile" : "Sign in to Orbit";
        System.Windows.Automation.AutomationProperties.SetName(ProfileNav, signedIn ? "Profile: " + name : "Profile: Sign in");
    }

    private async Task LoadOrbitProfileAsync()
    {
        try
        {
            orbitProfile = await RequireOrbitAccount().GetProfileAsync();
        }
        finally
        {
            await LoadOrbitAvatarAsync();
        }
    }

    private async Task LoadOrbitAvatarAsync()
    {
        var account = orbitAccount?.Account;
        orbitAvatar = null;
        if (account is null) return;
        try
        {
            var bytes = await RequireOrbitAccount().GetAvatarAsync();
            if (bytes is null && account.AvatarUrl.Length > 0)
                bytes = await orbitContent.DownloadAsync(account.AvatarUrl, 1024 * 1024, TimeSpan.FromSeconds(15));
            if (bytes is not null && orbitAccount?.Account?.Id == account.Id) orbitAvatar = DecodeProfilePicture(bytes);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or NotSupportedException or ArgumentException) { }
        UpdateProfileNav();
    }

    private static BitmapImage DecodeProfilePicture(byte[] bytes)
    {
        using
        var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || width > 10000 || height > 10000 || (long)width * height > 40000000)
            throw new IOException("Choose a smaller profile picture.");
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        if (width >= height) bitmap.DecodePixelWidth = Math.Min(width, 192);
        else bitmap.DecodePixelHeight = Math.Min(height, 192);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private async Task ChangeOrbitPictureAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose your Orbit profile picture",
            Filter = "Pictures|*.jpg;*.jpeg;*.png",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true)
        {
            orbitStatus.Text = "";
            return;
        }
        if (new FileInfo(picker.FileName).Length > 8 * 1024 * 1024) throw new IOException("Choose a picture under 8 MB.");
        BitmapImage picture;
        try
        {
            picture = DecodeProfilePicture(await File.ReadAllBytesAsync(picker.FileName));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            throw new IOException("This picture could not be opened. Choose a JPG or PNG.");
        }
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = 85
        };
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using
        var output = new MemoryStream();
        encoder.Save(output);
        await RequireOrbitAccount().SaveAvatarAsync(output.ToArray());
        orbitAvatar = picture;
        BuildOrbitAccount("Profile picture saved.");
    }

    private async Task SignOutOrbit(bool all)
    {
        string status = "Signed out.";
        try
        {
            await RequireOrbitAccount().SignOutAsync(all);
        }
        catch (Exception)
        {
            status = "Signed out locally. Server sign-out could not be confirmed; check your connection and saved credentials.";
        }
        orbitProfile = null;
        orbitAvatar = null;
        orbitDraftUsername = null;
        orbitDraftBio = null;
        BuildOrbitAccount(status);
    }

}
