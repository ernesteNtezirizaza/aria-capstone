import { NextResponse } from 'next/server';
import { z } from 'zod';
import prisma from '@/lib/prisma';
import { generateOtp, hashOtp, otpExpiryDate } from '@/lib/otp';
import { sendOtpEmail } from '@/lib/mailer';

const schema = z.object({
  email: z.string().email(),
  purpose: z.enum(['VERIFY_EMAIL', 'RESET_PASSWORD']),
});

const GENERIC_MESSAGE = 'If eligible, a new code has been sent to that email.';

export async function POST(req: Request) {
  const parsed = schema.safeParse(await req.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ error: 'Invalid request.' }, { status: 400 });
  }
  const { email, purpose } = parsed.data;

  const user = await prisma.user.findUnique({ where: { email: email.toLowerCase() } });

  const eligible =
    user &&
    ((purpose === 'VERIFY_EMAIL' && user.status === 'PENDING') ||
      (purpose === 'RESET_PASSWORD' && user.status === 'ACTIVE' && user.passwordHash));

  if (eligible && user) {
    const otp = generateOtp();
    const otpCodeHash = await hashOtp(otp);
    await prisma.user.update({
      where: { id: user.id },
      data: { otpCodeHash, otpPurpose: purpose, otpExpiresAt: otpExpiryDate() },
    });

    try {
      await sendOtpEmail(user.email, user.name, otp, purpose);
    } catch (err) {
      console.error('[resend-otp] Failed to send OTP email:', err);
      return NextResponse.json({ error: 'Could not send the email right now. Please try again shortly.' }, { status: 502 });
    }
  }

  return NextResponse.json({ success: true, message: GENERIC_MESSAGE });
}
