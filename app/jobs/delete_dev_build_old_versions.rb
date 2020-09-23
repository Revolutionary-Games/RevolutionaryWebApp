# frozen_string_literal: true

# Deletes devbuild upload versions where there are more than 1 and the other ones are more
# than a day old
class DeleteDevBuildOldVersions < ApplicationJob
  queue_as :default

  after_perform do |_job|
    # Queue again
    DeleteDevBuildOldVersions.set(wait: 24.hour).perform_later
  end

  def perform
    DevBuild.where('(SELECT COUNT(*) FROM storage_item_versions WHERE storage_item_id = ' \
                     'dev_builds.storage_item_id) > 1').each { |build|
      build.storage_item.storage_item_versions.each { |version|
        next unless version.uploading
        next if Time.now - version.created_at < 1.day

        Rails.logger.info "Build (#{build.id}) has multiple build file versions, " \
                          "deleting: version #{version.version} (#{version.id})"
        version.destroy
      }
    }
  end
end
