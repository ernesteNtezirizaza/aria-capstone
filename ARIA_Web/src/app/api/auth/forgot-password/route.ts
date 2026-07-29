import { NextResponse } from 'next/server';
import { z } from 'zod';
import prisma from '@/lib/prisma';
import { generateOtp, hashOtp, otpExpiryDate } from '@/lib/otp';
import { sendOtpEmail } from '@/lib/mailer';

const schema = z.object({ email: z.string().email() });

/* Always returns the same generic message regardless of whether the email
   exists or is eligible, to avoid leaking which addresses have accounts. */
const GENERIC_MESSAGE = 'If that email has an active ARIA account, a password reset code has been sent.';

export async function POST(req: Request) {
  const parsed = schema.safeParse(await req.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ error: 'Invalid request.' }, { status: 400 });
  }
  const { email } = parsed.data;

  const user = await prisma.user.findUnique({ where: { email: email.toLowerCase() } });

  if (user && user.status === 'ACTIVE' && user.passwordHash) {
    const otp = generateOtp();
    const otpCodeHash = await hashOtp(otp);
    await prisma.user.update({
      where: { id: user.id },
      data: { otpCodeHash, otpPurpose: 'RESET_PASSWORD', otpExpiresAt: otpExpiryDate() },
    });

    try {
      await sendOtpEmail(user.email, user.name, otp, 'RESET_PASSWORD');
    } catch (err) {
      console.error('[forgot-password] Failed to send OTP email:', err);
      return NextResponse.json({ error: 'Could not send the reset email right now. Please try again shortly.' }, { status: 502 });
    }
  }

  return NextResponse.json({ success: true, message: GENERIC_MESSAGE });
}
