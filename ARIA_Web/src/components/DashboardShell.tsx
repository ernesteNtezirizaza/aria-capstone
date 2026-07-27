import DashboardSidebar from './DashboardSidebar';

type SessionInfo = { name: string; role: 'ADMIN' | 'FORESTER' } | null;

export default function DashboardShell({
  session,
  title,
  subtitle,
  dataUnavailable,
  headerRight,
  children,
}: {
  session?: SessionInfo;
  title: string;
  subtitle: string;
  dataUnavailable?: boolean;
  headerRight?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col lg:flex-row min-h-screen bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-50">
      <DashboardSidebar session={session} />
      <div className="flex-1 min-w-0 max-w-7xl px-4 py-8">
        <header className="flex flex-col md:flex-row items-start md:items-center justify-between mb-10 gap-4">
          <div>
            <h1 className="text-3xl font-bold tracking-tight">{title}</h1>
            <p className="text-slate-500 dark:text-slate-400 mt-1">{subtitle}</p>
          </div>
          {headerRight && <div className="flex flex-wrap items-center gap-3">{headerRight}</div>}
        </header>

        {dataUnavailable && (
          <div className="mb-10 p-4 rounded-xl bg-amber-500/10 border border-amber-500/20 text-amber-700 dark:text-amber-300 text-sm">
            Telemetry data couldn&apos;t be loaded right now. This shows an empty view rather than an error page --
            try refreshing shortly, or check back once new simulation runs have posted data.
          </div>
        )}

        {children}
      </div>
    </div>
  );
}
