using Dojo.Framework.Events;
using Dojo.Framework.Startup;
using Dojo.Framework.Utilities;
using VContainer;
using VContainer.Unity;

namespace Dojo.Framework
{
    public sealed class FrameworkInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            builder.Register<IEventHub, EventHub>(Lifetime.Singleton);
            builder.Register<ILoadingService, LoadingService>(Lifetime.Singleton);

            // Singleton and on the root, because the Lobby's buttons and whatever reports a
            // preprocess outcome are in different scenes and must read the same instance.
            builder.Register<IAppReadiness, AppReadiness>(Lifetime.Singleton);
        }
    }
}
