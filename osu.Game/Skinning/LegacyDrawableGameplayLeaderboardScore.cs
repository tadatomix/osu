// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Layout;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Select.Leaderboards;
using osu.Game.Users;
using osu.Game.Users.Drawables;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Skinning
{
    public partial class LegacyDrawableGameplayLeaderboardScore : CompositeDrawable
    {
        public const float MIN_WIDTH = extended_left_panel_width + avatar_size / 2 + 5;

        private const float left_panel_extension_width = 20;

        private const float regular_left_panel_width = avatar_size + avatar_size / 2;
        private const float extended_left_panel_width = regular_left_panel_width + left_panel_extension_width;

        private const float accuracy_combo_width_cutoff = 150;
        private const float username_score_width_cutoff = 50;

        private const float avatar_size = PANEL_HEIGHT;

        // Height was taken from https://osu.ppy.sh/wiki/en/Skinning/Interface#song-selection
        // It was divided by the half, since otherwise panel would be extremely huge
        public const float PANEL_HEIGHT = 85f / 2f;

        /// <summary>
        /// Extra width lenience to account for the out-of-range values produced by elastic easing when the score panel becomes extended (due to earning first score position or is a tracked score).
        /// </summary>
        public const float ELASTIC_WIDTH_LENIENCE = 10f;

        private const double panel_transition_duration = 500;
        private const double text_transition_duration = 200;

        public Bindable<bool> Expanded { get; } = new BindableBool();

        public BindableLong TotalScore { get; } = new BindableLong();
        public BindableDouble Accuracy { get; } = new BindableDouble(1);
        public BindableInt Combo { get; } = new BindableInt();
        public BindableBool HasQuit { get; } = new BindableBool();
        public Bindable<int?> ScorePosition { get; } = new Bindable<int?>();
        public Bindable<long> DisplayOrder { get; } = new Bindable<long>();

        private Func<ScoringMode, long>? getDisplayScoreFunction;

        public Func<ScoringMode, long> GetDisplayScore
        {
            set => getDisplayScoreFunction = value;
        }

        public Color4? BackgroundColour { get; }

        public IUser? User { get; }

        /// <summary>
        /// Whether this score is the local user or a replay player (and should be focused / always visible).
        /// </summary>
        public readonly bool Tracked;

        private FillFlowContainer scorePanel = null!;
        private Container rightLayer = null!;
        private Box rightLayerGradient = null!;
        private Container positionComponent = null!;
        private Container avatarComponent = null!;
        private Container scoreComponents = null!;
        private OsuSpriteText usernameText = null!;
        private OsuSpriteText positionText = null!;
        private OsuSpriteText scoreText = null!;
        private OsuSpriteText comboText = null!;

        private IBindable<ScoringMode> scoreDisplayMode = null!;

        private bool isFriend;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private readonly LayoutValue drawSizeLayout = new LayoutValue(Invalidation.DrawSize);

        /// <summary>
        /// Creates a new <see cref="LegacyDrawableGameplayLeaderboardScore"/>.
        /// </summary>
        public LegacyDrawableGameplayLeaderboardScore(GameplayLeaderboardScore score)
        {
            User = score.User;
            Tracked = score.Tracked;
            TotalScore.BindTo(score.TotalScore);
            Accuracy.BindTo(score.Accuracy);
            Combo.BindTo(score.Combo);
            HasQuit.BindTo(score.HasQuit);
            ScorePosition.BindTo(score.Position);
            DisplayOrder.BindTo(score.DisplayOrder);
            GetDisplayScore = score.GetDisplayScore;

            if (score.TeamColour != null)
                BackgroundColour = score.TeamColour.Value;

            RelativeSizeAxes = Axes.X;
            Height = PANEL_HEIGHT;

            AddLayout(drawSizeLayout);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = scorePanel = new FillFlowContainer
            {
                BorderThickness = 2f,
                Masking = true,
                AutoSizeAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                Children = new[]
                {
                    // The left layer was removed due to being unused in LegacyLeaderboard
                    rightLayer = new Container
                    {
                        RelativeSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            rightLayerGradient = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                            },
                            //Avatar component has been moved inside the right layer, due to having noo need to be a separator between position and score
                            avatarComponent = new Container
                            {
                                Size = new Vector2(avatar_size),
                                Child = new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = true,
                                    Child = new ScoreAvatar(User)
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        RelativeSizeAxes = Axes.Both,
                                    },
                                },
                            },
                            positionComponent = new Container
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Width = regular_left_panel_width,
                                // This may not be mathematically accurate but the position text looks best aligned with it.
                                Padding = new MarginPadding { Right = avatar_size / 2 / 2 },
                                RelativeSizeAxes = Axes.Y,
                                Child = positionText = new OsuSpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                                }
                            },
                            scoreComponents = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Left = avatar_size + 4, Right = 20, Vertical = 5 },
                                Children = new Drawable[]
                                {
                                    new GridContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        ColumnDimensions = new[]
                                        {
                                            new Dimension(),
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        RowDimensions = new[]
                                        {
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        Content = new[]
                                        {
                                            new[]
                                            {
                                                usernameText = new TruncatingSpriteText
                                                {
                                                    Anchor = Anchor.BottomLeft,
                                                    Origin = Anchor.BottomLeft,
                                                    Text = User?.Username ?? string.Empty,
                                                    Font = OsuFont.Style.Caption1,
                                                    RelativeSizeAxes = Axes.X,
                                                }
                                            }
                                        },
                                    },
                                    new GridContainer
                                    {
                                        Anchor = Anchor.BottomLeft,
                                        Origin = Anchor.BottomLeft,
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        ColumnDimensions = new[]
                                        {
                                            new Dimension(),
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        RowDimensions = new[]
                                        {
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        Content = new[]
                                        {
                                            new[]
                                            {
                                                scoreText = new TruncatingSpriteText
                                                {
                                                    Anchor = Anchor.BottomLeft,
                                                    Origin = Anchor.BottomLeft,
                                                    Font = OsuFont.Style.Body,
                                                    RelativeSizeAxes = Axes.X,
                                                },
                                                comboText = new OsuSpriteText
                                                {
                                                    Anchor = Anchor.BottomRight,
                                                    Origin = Anchor.BottomRight,
                                                    Font = OsuFont.Style.Caption2,
                                                },
                                            }
                                        },
                                    },
                                },
                            }
                        }
                    },
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            isFriend = User != null && api.Friends.Any(u => User.OnlineID == u.TargetID);

            scoreDisplayMode = config.GetBindable<ScoringMode>(OsuSetting.ScoreDisplayMode);
            scoreDisplayMode.BindValueChanged(_ => updateScore());
            TotalScore.BindValueChanged(_ => updateScore(), true);

            Combo.BindValueChanged(v => comboText.Text = $@"{v.NewValue}x", true);

            Expanded.BindValueChanged(onExpanded, true);

            HasQuit.BindValueChanged(_ => updatePanelState());
            ScorePosition.BindValueChanged(_ => updatePanelState(), true);

            FinishTransforms(true);
        }

        private void updateScore() => scoreText.Text = (getDisplayScoreFunction?.Invoke(scoreDisplayMode.Value) ?? TotalScore.Value).ToString("N0");

        private void onExpanded(ValueChangedEvent<bool> expanded)
        {
            if (expanded.NewValue)
            {
                rightLayer.ResizeWidthTo(computeRightLayerWidth(), panel_transition_duration, Easing.OutQuint);
                scoreComponents.FadeIn(panel_transition_duration, Easing.OutQuint);
            }
            else
            {
                rightLayer.ResizeWidthTo(avatar_size / 2, panel_transition_duration, Easing.OutQuint);
                scoreComponents.FadeOut(text_transition_duration, Easing.OutQuint);
            }
        }

        private void updatePanelState()
        {
            positionText.Text = ScorePosition.Value.HasValue ? $"{ScorePosition.Value.Value.FormatRank()}" : "-";

            Color4 usernameColour = Color4.White;
            Color4 comboColour = new Color4(153, 250, 255, 255);
            //Width extension has been removed due to never being used in this panel

            if (HasQuit.Value)
            {
                setPanelColour(Color4.Gray);
                usernameColour = new Color4(236, 39, 81, 255);
            }
            else if (ScorePosition.Value == 1)
            {
                setPanelColour(BackgroundColour ?? new Color4(97, 190, 255, 150));
            }
            else if (Tracked)
            {
                setPanelColourAsTracked();
                setPanelColour(BackgroundColour ?? new Color4(250, 250, 250, 100));
            }
            else if (isFriend)
            {
                setPanelColour(BackgroundColour ?? new Color4(255, 97, 175, 180));
            }
            else
                setPanelColour(BackgroundColour ?? new Color4(31, 115, 153, 150));

            usernameText.FadeColour(usernameColour, text_transition_duration, Easing.OutQuint);
            comboText.FadeColour(comboColour);
        }

        private void setPanelColour(Color4 baseColour)
        {
            rightLayerGradient.Colour = ColourInfo.GradientVertical(baseColour.Opacity(0.1f), baseColour.Opacity(0.3f));
            scorePanel.BorderColour = ColourInfo.GradientVertical(baseColour.Opacity(0.2f), baseColour);
        }

        private void setPanelColourAsTracked()
        {
            rightLayerGradient.Colour = ColourInfo.GradientVertical(colours.Blue4.Opacity(0.25f), colours.Blue3.Opacity(0.6f));
            scorePanel.BorderColour = ColourInfo.GradientVertical(colours.Blue1.Opacity(0.2f), colours.Blue1);
        }

        protected override void Update()
        {
            base.Update();

            if (!drawSizeLayout.IsValid)
            {
                if (Expanded.Value)
                {
                    rightLayer.ClearTransforms(targetMember: nameof(Width));
                    rightLayer.Width = computeRightLayerWidth();
                }

                drawSizeLayout.Validate();
            }

            bool showAccuracyAndCombo = rightLayer.Width >= accuracy_combo_width_cutoff;

            comboText.Alpha = showAccuracyAndCombo ? 1 : 0;

            bool showUsernameAndScore = rightLayer.Width >= username_score_width_cutoff;

            usernameText.Alpha = showUsernameAndScore ? 1 : 0;
            scoreText.Alpha = showUsernameAndScore ? 1 : 0;
        }

        private float computeRightLayerWidth() => Math.Max(0, DrawWidth - extended_left_panel_width - avatar_size / 2);

        private partial class ScoreAvatar : CompositeDrawable
        {
            private readonly IUser? user;

            private Box placeholder = null!;

            public ScoreAvatar(IUser? user)
            {
                this.user = user;

                RelativeSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = placeholder = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.Gray(0.1f),
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                LoadComponentAsync(new DrawableAvatar(user), a =>
                {
                    placeholder.FadeOut(300, Easing.InQuint);
                    AddInternal(a);
                });
            }
        }
    }
}
