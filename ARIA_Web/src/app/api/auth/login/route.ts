import { NextResponse } from 'next/server';
import { z } from 'zod';
import prisma from '@/lib/prisma';
import { verifyPassword } from '@/lib/password';
import { createSessionToken, setSessionCookie } from '@/lib/auth';

const schema = z.object({
  email: z.string().email(),
  password: z.string().min(1),
});

export async function POST(req: Request) {
  const parsed = schema.safeParse(await req.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ error: 'Invalid email or password.' }, { status: 400 });
  }
  const { email, password } = parsed.data;

  const user = await prisma.user.findUnique({ where: { email: email.toLowerCase() } });

  if (!user || !user.passwordHash) {
    if (user && user.status === 'PENDING') {
      return NextResponse.json(
        { error: 'This account has not been activated yet. Check your email for the verification code.' },
        { status: 403 }
      );
    }
    return NextResponse.json({ error: 'Invalid email or password.' }, { status: 401 });
  }

  if (user.status === 'DISABLED') {
    return NextResponse.json({ error: 'This account has been disabled. Contact an administrator.' }, { status: 403 });
  }

  const valid = await verifyPassword(password, user.passwordHash);
  if (!valid) {
    return NextResponse.json({ error: 'Invalid email or password.' }, { status: 401 });
  }

  const token = await createSessionToken({
    sub: String(user.id),
    email: user.email,
    name: user.name,
    role: user.role,
  });
  await setSessionCookie(token);

  return NextResponse.json({ success: true, role: user.role });
}
