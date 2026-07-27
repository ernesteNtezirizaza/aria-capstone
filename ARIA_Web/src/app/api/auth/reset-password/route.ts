import { NextResponse } from 'next/server';
import { z } from 'zod';
import prisma from '@/lib/prisma';
import { verifyOtp } from '@/lib/otp';
import { hashPassword } from '@/lib/password';

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

  const user = await prisma.user.findUnique({ where: { email: email.toLowerCase() } });

  if (
    !user ||
    user.otpPurpose !== 'RESET_PASSWORD' ||
    !user.otpCodeHash ||
    !user.otpExpiresAt ||
    user.otpExpiresAt < new Date()
  ) {
    return NextResponse.json({ error: 'Invalid or expired reset code.' }, { status: 400 });
  }

  const otpValid = await verifyOtp(otp, user.otpCodeHash);
  if (!otpValid) {
    return NextResponse.json({ error: 'Invalid or expired reset code.' }, { status: 400 });
  }

  const passwordHash = await hashPassword(password);
  await prisma.user.update({
    where: { id: user.id },
    data: { passwordHash, otpCodeHash: null, otpPurpose: null, otpExpiresAt: null },
  });

  return NextResponse.json({ success: true });
}
