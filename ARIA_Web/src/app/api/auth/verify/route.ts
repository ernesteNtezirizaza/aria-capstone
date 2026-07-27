import { NextResponse } from 'next/server';
import { z } from 'zod';
import prisma from '@/lib/prisma';
import { verifyOtp } from '@/lib/otp';
import { hashPassword, isPasswordStrongEnough } from '@/lib/password';
import { createSessionToken, setSessionCookie } from '@/lib/auth';

const schema = z.object({
  email: z.string().email(),
  otp: z.string().length(6),
  password: z.string().min(8, 'Password must be at least 8 characters.'),
});

export async function POST(req: Request) {
  const parsed = schema.safeParse(await req.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ error: parsed.error.issues[0]?.message || 'Invalid request.' }, { status: 400 });
  }
  const { email, otp, password } = parsed.data;

  if (!isPasswordStrongEnough(password)) {
    return NextResponse.json({ error: 'Password must be at least 8 characters.' }, { status: 400 });
  }

  const user = await prisma.user.findUnique({ where: { email: email.toLowerCase() } });

  if (
    !user ||
    user.otpPurpose !== 'VERIFY_EMAIL' ||
    !user.otpCodeHash ||
    !user.otpExpiresAt ||
    user.otpExpiresAt < new Date()
  ) {
    return NextResponse.json({ error: 'Invalid or expired verification code.' }, { status: 400 });
  }

  const otpValid = await verifyOtp(otp, user.otpCodeHash);
  if (!otpValid) {
    return NextResponse.json({ error: 'Invalid or expired verification code.' }, { status: 400 });
  }

  const passwordHash = await hashPassword(password);
  const updated = await prisma.user.update({
    where: { id: user.id },
    data: {
      passwordHash,
      status: 'ACTIVE',
      otpCodeHash: null,
      otpPurpose: null,
      otpExpiresAt: null,
    },
  });

  const token = await createSessionToken({
    sub: String(updated.id),
    email: updated.email,
    name: updated.name,
    role: updated.role,
  });
  await setSessionCookie(token);

  return NextResponse.json({ success: true, role: updated.role });
}
