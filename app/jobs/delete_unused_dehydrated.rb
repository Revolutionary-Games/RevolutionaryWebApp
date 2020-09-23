# frozen_string_literal: true

# Deletes dehydrated objects that are unreferenced
class DeleteUnusedDehydrated < ApplicationJob
  queue_as :default

  after_perform do |_job|
    # Queue again
    DeleteUnusedDehydrated.set(wait: 1.week).perform_later
  end

  def perform
    # TODO: might be better to use a join here to do this query
    DehydratedObject.where("NOT EXISTS(SELECT 1 FROM dehydrated_objects_dev_builds WHERE " +
                             "dehydrated_object_id = dehydrated_objects.id)").each { |object|
      Rails.logger.info "Deleting unused dehydrated object #{object.id}"
      object.destroy
    }
  end
end
