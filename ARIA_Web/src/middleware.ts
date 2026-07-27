import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';
import { verifySessionToken, SESSION_COOKIE_NAME } from '@/lib/auth';

const ADMIN_ONLY_PREFIXES = ['/admin'];
// '/simulation' is deliberately an EXACT match only, not a '/simulation/*'
// prefix: the Unity WebGL build's own static assets are served from
// public/simulation/ (index.html, Build/*.gz, etc.), which share that same
// URL prefix. A wildcard here redirected those asset requests to /login
// instead of serving the actual files, breaking the embedded simulation
// entirely -- the page route itself already checks the session server-side
// (see simulation/page.tsx), so only that exact path needs gating here.
const EXACT_PROTECTED_PATHS = ['/simulation'];
const PROTECTED_PREFIXES = ['/dashboard', '/admin'];

export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  const isProtected =
    EXACT_PROTECTED_PATHS.includes(pathname) ||
    PROTECTED_PREFIXES.some((p) => pathname === p || pathname.startsWith(`${p}/`));
  if (!isProtected) return NextResponse.next();

  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  const session = token ? await verifySessionToken(token) : null;

  if (!session) {
    const loginUrl = new URL('/login', request.url);
    loginUrl.searchParams.set('from', pathname);
    return NextResponse.redirect(loginUrl);
  }

  const isAdminOnly = ADMIN_ONLY_PREFIXES.some((p) => pathname === p || pathname.startsWith(`${p}/`));
  if (isAdminOnly && session.role !== 'ADMIN') {
    return NextResponse.redirect(new URL('/dashboard', request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: ['/dashboard/:path*', '/simulation', '/admin/:path*'],
};
