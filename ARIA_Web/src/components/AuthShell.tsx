import Image from 'next/image';
import Link from 'next/link';

export default function AuthShell({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-950 text-slate-50 px-4 py-12">
      <div className="w-full max-w-md">
        <Link href="/" className="flex justify-center mb-8">
          <Image src="/logo/logo.png" alt="ARIA Logo" width={200} height={64} className="object-contain h-12 w-auto" priority />
        </Link>
        <div className="rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm p-6 sm:p-8">
          <h1 className="text-xl font-semibold text-white mb-1">{title}</h1>
          <p className="text-sm text-slate-400 mb-6">{subtitle}</p>
          {children}
        </div>
      </div>
    </div>
  );
}
