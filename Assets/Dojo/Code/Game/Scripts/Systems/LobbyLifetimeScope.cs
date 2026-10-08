using Dojo.Framework.Components;
using Dojo.Framework.UI;
using Dojo.Game.Managers;
using Dojo.Game.UI;
using Dojo.Game.UI.Account;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Dojo.Game.Systems
{
    
    public sealed class LobbyLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterBuildCallback(container =>
            {
                foreach (var toLoad in FindObjectsByType<ToLoad>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(toLoad);
                }

                // The main menu dims itself until startup readiness says the app is usable, so it
                // needs the same injection ToLoad gets. Without this it sees no IAppReadiness,
                // treats itself as standalone, and lights up while content is still downloading.
                foreach (var menu in FindObjectsByType<LobbyMainMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(menu);
                }

                // The menu and the two sign-in cards show and hide from the root scope's
                // SignInGate, which only reaches them through injection.
                foreach (var screens in FindObjectsByType<LobbyScreens>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(screens);
                }

                // The session panel reads the connection, the signed-in player and the content,
                // all of which live on the root scope.
                foreach (var panel in FindObjectsByType<SessionPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(panel);
                }

                // The home sidebar's world list and the account chip: the world source and the
                // WorkOS email, both on the root scope.
                foreach (var recent in FindObjectsByType<RecentWorldsPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(recent);
                }

                foreach (var chip in FindObjectsByType<AccountChip>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    container.Inject(chip);
                }

                container.Resolve<IDialogManager>();
                container.Resolve<PanelManager>();
            });
        }
    }
}
