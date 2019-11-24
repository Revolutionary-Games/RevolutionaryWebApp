# frozen_string_literal: true

class ApplicationMailer < ActionMailer::Base
  default from: MailFromHelper.from
  layout 'mailer'
end
