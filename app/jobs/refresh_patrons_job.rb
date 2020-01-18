# frozen_string_literal: true

# Refreshes all patron info and applies groups
class RefreshPatronsJob < ApplicationJob
  queue_as :default

  after_perform do |_job|
    # Queue again
    RefreshPatronsJob.set(wait: 1.hour).perform_later
  end

  def perform
    PatreonSettings.all.each { |settings|
      puts 'checking a patreon settings'
      next unless settings.active

      puts 'it is active'
      # TODO: actual refresh
      patrons = settings.all_patrons
      puts "Total patron count: #{patrons.size}"

      puts 'finish checking'
      settings.last_refreshed = Time.now
      settings.save
    }
  end
end
