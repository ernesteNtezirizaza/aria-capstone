import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import { getSession } from '@/lib/auth';

export const metadata = {
  title: 'Live Simulation | ARIA',
};

export const dynamic = 'force-dynamic';

export default async function SimulationPage() {
  const session = await getSession();
  /* Tags telemetry this session posts with the logged-in user, so Admins
     can see per-user simulation activity on the dashboard. The Unity build
     reads this from the iframe's own query string (Application.absoluteURL). */
  const src = session ? `/simulation/index.html?uid=${session.sub}` : '/simulation/index.html';

  return (
    <div className="h-screen flex flex-col bg-slate-950 text-slate-50">
      <header className="flex items-center justify-between px-4 sm:px-8 py-3 border-b border-slate-800 bg-slate-950/95 backdrop-blur-md shrink-0">
        <Link href="/" className="inline-flex items-center text-sm text-slate-300 hover:text-white transition-colors">
          <ArrowLeft className="w-4 h-4 mr-1" /> Back to Home
        </Link>
        <h1 className="text-sm font-medium text-slate-300 hidden sm:block">ARIA Live Simulation</h1>
        <Link
          href="/dashboard"
          className="px-4 py-1.5 rounded-full bg-white/10 hover:bg-white/20 text-white text-sm transition-all"
        >
          Dashboard
        </Link>
      </header>
      <main className="flex-1 min-h-0">
        <iframe
          src={src}
          title="ARIA Unity Simulation"
          className="w-full h-full border-0 block"
          allow="autoplay; fullscreen"
        />
      </main>
    </div>
  );
}
