import { NextResponse } from 'next/server';
import { z } from 'zod';
import prisma from '@/lib/prisma';
import { getSession } from '@/lib/auth';
import { generateOtp, hashOtp, otpExpiryDate } from '@/lib/otp';
import { sendOtpEmail } from '@/lib/mailer';

const createSchema = z.object({
  name: z.string().min(1, 'Name is required.'),
  email: z.string().email(),
  role: z.enum(['ADMIN', 'FORESTER']),
});

export async function GET() {
  const session = await getSession();
  if (!session || session.role !== 'ADMIN') {
    return NextResponse.json({ error: 'Forbidden.' }, { status: 403 });
  }

  const users = await prisma.user.findMany({
    orderBy: { createdAt: 'desc' },
    select: {
      id: true,
      name: true,
      email: true,
      role: true,
      status: true,
      createdAt: true,
      createdBy: { select: { name: true, email: true } },
      _count: { select: { episodes: true } },
    },
  });

  return NextResponse.json({ users });
}

export async function POST(req: Request) {
  const session = await getSession();
  if (!session || session.role !== 'ADMIN') {
    return NextResponse.json({ error: 'Forbidden.' }, { status: 403 });
  }

  const parsed = createSchema.safeParse(await req.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ error: parsed.error.issues[0]?.message || 'Invalid request.' }, { status: 400 });
  }
  const { name, email, role } = parsed.data;
  const normalizedEmail = email.toLowerCase();

  const existing = await prisma.user.findUnique({ where: { email: normalizedEmail } });
  if (existing) {
    return NextResponse.json({ error: 'A user with that email already exists.' }, { status: 409 });
  }

  const otp = generateOtp();
  const otpCodeHash = await hashOtp(otp);

  const user = await prisma.user.create({
    data: {
      name,
      email: normalizedEmail,
      role,
      status: 'PENDING',
      otpCodeHash,
      otpPurpose: 'VERIFY_EMAIL',
      otpExpiresAt: otpExpiryDate(),
      createdById: Number(session.sub),
    },
  });

  try {
    await sendOtpEmail(user.email, user.name, otp, 'VERIFY_EMAIL');
  } catch (err) {
    console.error('[admin/users] Failed to send verification email:', err);
    return NextResponse.json(
      {
        error:
          'User created, but the verification email could not be sent. Use "Resend invitation" once email is working.',
        user: { id: user.id, name: user.name, email: user.email, role: user.role, status: user.status },
      },
      { status: 207 }
    );
  }

  return NextResponse.json({
    success: true,
    user: { id: user.id, name: user.name, email: user.email, role: user.role, status: user.status },
  });
}
