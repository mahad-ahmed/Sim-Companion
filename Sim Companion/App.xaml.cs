using System.Threading;
using System.Windows;

namespace Sim_Companion {
    public partial class App : Application {
        private const string UUID_MUTEX = "{42d8e451-bbd5-48e6-9f4b-9c9dc55737a2} com.atompunkapps.sim_companion.mutex";
        private const string UUID_EVENT = "{42d8e451-bbd5-48e6-9f4b-9c9dc55737a2} com.atompunkapps.sim_companion.event";

        private Mutex mutex;
        private EventWaitHandle eventWaitHandle;

        private void Application_Startup(object sender, StartupEventArgs e) {
            bool isOwner;
            mutex = new Mutex(true, UUID_MUTEX, out isOwner);
            eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, UUID_EVENT);

            System.GC.KeepAlive(mutex);

            if(isOwner) {
                Thread t = new Thread(() => {
                    while(eventWaitHandle.WaitOne()) {
                        Current.Dispatcher.BeginInvoke((System.Action)(() => ((MainWindow)Current.MainWindow).BringToFront()));
                    }
                });
                t.IsBackground = true;
                t.Start();
                return;
            }

            eventWaitHandle.Set();
            Shutdown();
        }
    }
}
