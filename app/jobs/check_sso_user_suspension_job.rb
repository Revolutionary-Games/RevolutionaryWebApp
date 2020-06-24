# frozen_string_literal: true

# Checks whether a User should be automatically suspended based on sso status
class CheckSsoUserSuspensionJob < ApplicationJob
  queue_as :default
  include PatreonGroupHelper

  def perform(email)
    user = User.find_by email: email

    unless user
      logger.info "User to check SSO suspend status for doesn't exist, skipping job"
      return
    end

    SsoSuspendHandler.check_user user
  end
end
