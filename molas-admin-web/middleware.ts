import { NextRequest, NextResponse } from 'next/server';

const PROTECTED_PREFIX = ['/(dashboard)', '/replenishment', '/recommendations', '/reports', '/settings'];
const PUBLIC_PATHS = ['/login', '/api/auth'];

function isProtected(pathname: string): boolean {
  if (PUBLIC_PATHS.some((p) => pathname.startsWith(p))) return false;
  if (pathname === '/') return false;
  // The (dashboard) route group strips the group name in URL
  // so all paths except /login and /api/** are protected
  return !pathname.startsWith('/api/auth');
}

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  // Allow public paths
  if (pathname.startsWith('/api/auth') || pathname === '/login') {
    return NextResponse.next();
  }

  // Allow static assets and Next.js internals
  if (
    pathname.startsWith('/_next') ||
    pathname.startsWith('/favicon') ||
    pathname.includes('.')
  ) {
    return NextResponse.next();
  }

  const token = request.cookies.get('molas_token')?.value;

  if (!token) {
    const loginUrl = new URL('/login', request.url);
    loginUrl.searchParams.set('redirect', pathname);
    return NextResponse.redirect(loginUrl);
  }

  // Forward the token as Authorization header for server components / route handlers
  const requestHeaders = new Headers(request.headers);
  requestHeaders.set('x-molas-token', token);

  return NextResponse.next({
    request: { headers: requestHeaders },
  });
}

export const config = {
  matcher: [
    /*
     * Match all request paths except for the ones starting with:
     * - _next/static (static files)
     * - _next/image (image optimization files)
     * - favicon.ico (favicon file)
     */
    '/((?!_next/static|_next/image|favicon.ico).*)',
  ],
};
