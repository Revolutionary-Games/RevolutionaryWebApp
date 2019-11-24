# frozen_string_literal: true

# Sends test emails
class TestMailer < ActionMailer::Base
  default from: 'from@example.com'

  def test_message(to)
    mail(
      to: to,
      subject: '[ThriveDevCenter] Email delivery test'
    ) { |format|
      format.text
      format.html
    }
  end
end
