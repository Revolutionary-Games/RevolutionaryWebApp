# frozen_string_literal: true

# Allows admins to send a test email to any address
class SendTestEmail < Hyperstack::ServerOp
  param :acting_user
  param :email
  add_error(:email, :is_invalid, 'target email is not valid') {
    params.email !~ URI::MailTo::EMAIL_REGEXP
  }
  validate { params.acting_user.admin? }
  step {
    TestMailer.test_message(params.email).deliver_later
  }
end
