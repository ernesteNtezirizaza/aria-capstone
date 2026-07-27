'use client';

import { useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import Link from 'next/link';
import Image from 'next/image';
import { LayoutDashboard, Rocket, UserCog, Skull, BarChart3, Database, LogOut, Menu, X, ArrowLeft } from 'lucide-react';

type SessionInfo = { name: string; role: 'ADMIN' | 'FORESTER' } | null;

const NAV_ITEMS = [
  // exact: true for '/dashboard' -- otherwise every sibling sub-page below
  // (failures/activity/episodes, which all share the '/dashboard' prefix
  // but are NOT children of the overview page) would prefix-match it too,
  // highlighting "Dashboard" simultaneously with whichever page is active.
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard, adminOnly: false, exact: true },
  { href: '/dashboard/failures', label: 'Failures & Reseeds', icon: Skull, adminOnly: false },
  { href: '/dashboard/activity', label: 'User Activity', icon: BarChart3, adminOnly: true },
  { href: '/dashboard/episodes', label: 'Episodes Log', icon: Database, adminOnly: false },
  { href: '/simulation', label: 'Simulation', icon: Rocket, adminOnly: false },
  { href: '/admin/users', label: 'User Management', icon: UserCog, adminOnly: true },
];

export default function DashboardSidebar({ session }: { session?: SessionInfo }) {
  const pathname = usePathname();
  const router = useRouter();
  const [mobileOpen, setMobileOpen] = useState(false);

  async function handleLogout() {
    await fetch('/api/auth/logout', { method: 'POST' });
    router.push('/login');
    router.refresh();
  }

  const items = NAV_ITEMS.filter((item) => !item.adminOnly || session?.role === 'ADMIN');

  const content = (
    <div className="flex flex-col h-full bg-slate-950 text-slate-50 border-r border-slate-800">
      <div className="p-5 border-b border-slate-800">
        <Link href="/" className="flex items-center gap-2 text-sm text-slate-400 hover:text-white transition-colors mb-4">
          <ArrowLeft className="w-4 h-4" />
          Back to Home
        </Link>
        <Image src="/logo/logo.png" alt="ARIA Logo" width={200} height={64} className="h-9 w-auto object-contain" priority />
      </div>

      <nav className="flex-1 p-3 space-y-1 overflow-y-auto">
        {items.map((item) => {
          const active = item.exact
            ? pathname === item.href
            : pathname === item.href || pathname?.startsWith(`${item.href}/`);
          const Icon = item.icon;
          return (
            <Link
              key={item.href}
              href={item.href}
              onClick={() => setMobileOpen(false)}
              className={`flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-colors ${
                active
                  ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                  : 'text-slate-400 hover:text-white hover:bg-white/5 border border-transparent'
              }`}
            >
              <Icon className="w-4 h-4 shrink-0" />
              {item.label}
            </Link>
          );
        })}
      </nav>

      {session && (
        <div className="p-3 border-t border-slate-800">
          <div className="flex items-center justify-between gap-2 px-3.5 py-3 rounded-xl bg-white/5">
            <div className="min-w-0">
              <p className="text-sm font-medium text-white truncate">{session.name}</p>
              <p className="text-xs text-slate-500">{session.role === 'ADMIN' ? 'Admin' : 'Forester'}</p>
            </div>
            <button
              onClick={handleLogout}
              aria-label="Log out"
              title="Log out"
              className="shrink-0 p-2 rounded-lg hover:bg-white/10 text-slate-400 hover:text-red-400 transition-colors"
            >
              <LogOut className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );

  return (
    <>
      {/* Mobile top bar */}
      <div className="lg:hidden sticky top-0 z-40 flex items-center justify-between px-4 py-3 bg-slate-950 border-b border-slate-800">
        <Image src="/logo/logo.png" alt="ARIA Logo" width={160} height={52} className="h-8 w-auto object-contain" />
        <button
          onClick={() => setMobileOpen(true)}
          aria-label="Open menu"
          className="p-2 rounded-lg hover:bg-white/10 text-slate-300 hover:text-white transition-colors"
        >
          <Menu className="w-5 h-5" />
        </button>
      </div>

      {/* Mobile drawer */}
      {mobileOpen && (
        <div className="lg:hidden fixed inset-0 z-50 flex">
          <div className="w-72 shrink-0 relative">
            {content}
            <button
              onClick={() => setMobileOpen(false)}
              aria-label="Close menu"
              className="absolute top-4 right-4 p-1.5 rounded-full hover:bg-white/10 text-slate-400 hover:text-white"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
          <button
            aria-label="Close menu overlay"
            onClick={() => setMobileOpen(false)}
            className="flex-1 bg-black/60 backdrop-blur-sm"
          />
        </div>
      )}

      {/* Desktop sidebar */}
      <div className="hidden lg:block w-64 shrink-0 sticky top-0 h-screen">{content}</div>
    </>
  );
}
