# frozen_string_literal: true

# Returns the right FROM for mailers
module MailFromHelper
  def self.from
    if !ENV['EMAIL_FROM']
      'ThriveDevCenter <thrivedevcenter@localhost>'
    else
      ENV['EMAIL_FROM']
    end
  end
end
