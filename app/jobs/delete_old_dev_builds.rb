# frozen_string_literal: true

# Deletes devbuilds that are not marked as kept and are older than 90 days
class DeleteOldDevBuilds < ApplicationJob
  queue_as :default

  after_perform do |_job|
    # Queue again
    DeleteOldDevBuilds.set(wait: 24.hour).perform_later
  end

  def perform
    DevBuild.where(important: false, keep: false).where("created_at < ?", 90.days.ago).each { |build|
      Rails.logger.info "Build (#{build.id}) is being deleted because it is old and not kept"
      build.destroy
    }
  end
end
