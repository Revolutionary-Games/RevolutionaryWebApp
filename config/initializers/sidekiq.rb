# frozen_string_literal: true

Sidekiq.configure_server do |config|
  config.redis = { url: 'redis://localhost:6379/0',
                   namespace: "thrivedevcenter_sidekiq_#{Rails.env}" }
end

Sidekiq.configure_client do |config|
  config.redis = { url: 'redis://localhost:6379/0',
                   namespace: "thrivedevcenter_sidekiq_#{Rails.env}" }

  Rails.application.config.after_initialize do
    # Queue job for starting all the right jobs
    StartTasksJob.perform_later
  end
end
